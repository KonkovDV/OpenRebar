using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenRebar.Domain;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;
using OpenRebar.Domain.Ports;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.IO;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MaterialResource;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.PropertyResource;
using Xbim.Ifc4.QuantityResource;
using Xbim.Ifc4.RepresentationResource;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.Ifc4.StructuralElementsDomain;
using Xbim.Ifc4.UtilityResource;

namespace OpenRebar.Infrastructure.Export;

/// <summary>
/// IFC4 exporter based on xBIM Essentials.
/// </summary>
public sealed class XbimIfcExporter : IIfcExporter
{
  private const double MillimetersToMeters = 1.0 / 1000.0;

  public Task ExportAsync(
      IReadOnlyList<ReinforcementZone> zones,
      SlabGeometry slab,
      string outputPath,
      CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(outputPath))
      throw new ArgumentException("Output path is required.", nameof(outputPath));

    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(directory))
      Directory.CreateDirectory(directory);

    var editor = new XbimEditorCredentials
    {
      ApplicationDevelopersName = "OpenRebar Development",
      ApplicationFullName = "OpenRebar Reinforcement",
      ApplicationIdentifier = "OpenRebar.Reinforcement",
      ApplicationVersion = ProductVersion.Current,
      EditorsIdentifier = "OpenRebar@local",
      EditorsFamilyName = "Automation",
      EditorsGivenName = "OpenRebar",
      EditorsOrganisationIdentifier = "OpenRebar",
      EditorsOrganisationName = "OpenRebar Development"
    };

    using var model = IfcStore.Create(editor, Xbim.Common.Step21.XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
    using var txn = model.BeginTransaction("OpenRebar reinforcement export");

    var project = model.Instances.New<IfcProject>(p =>
    {
      p.Name = "OpenRebar Reinforcement Export";
    });
    project.Initialize(ProjectUnits.SIUnitsUK);

    var site = model.Instances.New<IfcSite>(s =>
    {
      s.Name = "OpenRebar Site";
      s.CompositionType = IfcElementCompositionEnum.ELEMENT;
    });
    var building = model.Instances.New<IfcBuilding>(b =>
    {
      b.Name = "OpenRebar Building";
      b.CompositionType = IfcElementCompositionEnum.ELEMENT;
    });
    var storey = model.Instances.New<IfcBuildingStorey>(s =>
    {
      s.Name = "Reinforcement Storey";
      s.CompositionType = IfcElementCompositionEnum.ELEMENT;
      s.Elevation = 0;
    });

    model.Instances.New<IfcRelAggregates>(rel =>
    {
      rel.RelatingObject = project;
      rel.RelatedObjects.Add(site);
    });
    model.Instances.New<IfcRelAggregates>(rel =>
    {
      rel.RelatingObject = site;
      rel.RelatedObjects.Add(building);
    });
    model.Instances.New<IfcRelAggregates>(rel =>
    {
      rel.RelatingObject = building;
      rel.RelatedObjects.Add(storey);
    });

    var slabElement = CreateSlab(model, slab);
    model.Instances.New<IfcRelContainedInSpatialStructure>(rel =>
    {
      rel.RelatingStructure = storey;
      rel.RelatedElements.Add(slabElement);
    });

    var materialCache = new Dictionary<string, IfcMaterial>(StringComparer.OrdinalIgnoreCase);
    var typeCache = new Dictionary<string, IfcReinforcingBarType>(StringComparer.Ordinal);
    var context = model.Instances.OfType<IfcGeometricRepresentationContext>()
        .First(item => item.CoordinateSpaceDimension == 3);

    foreach (var zone in zones)
    {
      ct.ThrowIfCancellationRequested();

      for (int index = 0; index < zone.Rebars.Count; index++)
      {
        RebarSegment segment = zone.Rebars[index];
        var bar = model.Instances.New<IfcReinforcingBar>(entity =>
        {
          entity.Name = BarName(zone, segment, index);
          entity.GlobalId = BarGlobalId(zone, segment, index);
          entity.NominalDiameter = segment.DiameterMm * MillimetersToMeters;
          entity.BarLength = segment.TotalLength * MillimetersToMeters;
          entity.SteelGrade = zone.Spec.SteelClass;
        });

        model.Instances.New<IfcRelContainedInSpatialStructure>(rel =>
        {
          rel.RelatingStructure = storey;
          rel.RelatedElements.Add(bar);
        });

        AttachBarProperties(model, bar, zone, segment);
        AttachBarQuantities(model, bar, segment);
        AttachBarMaterial(model, bar, zone.Spec.SteelClass, materialCache);
        AttachBarType(model, bar, zone, segment, typeCache);
        AttachBarShape(model, bar, zone, segment, slab, context);
      }
    }

    txn.Commit();
    model.SaveAs(outputPath);

    return Task.CompletedTask;
  }

  private static string BarName(ReinforcementZone zone, RebarSegment segment, int index)
  {
    if (!string.IsNullOrWhiteSpace(segment.BarId))
      return segment.BarId;
    if (!string.IsNullOrWhiteSpace(segment.Mark))
      return segment.Mark;
    return $"BAR-{zone.Id}-{index}";
  }

  private static IfcGloballyUniqueId BarGlobalId(ReinforcementZone zone, RebarSegment segment, int index)
  {
    string key = string.Create(
        CultureInfo.InvariantCulture,
        $"{segment.BarId}|{zone.Id}|{index}|{zone.Layer}|{zone.Direction}|{segment.DiameterMm}|{segment.Start.X:R}|{segment.Start.Y:R}|{segment.End.X:R}|{segment.End.Y:R}");
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
    return IfcGloballyUniqueId.ConvertToBase64(new Guid(hash.AsSpan(0, 16)));
  }

  private static IfcSlab CreateSlab(IfcStore model, SlabGeometry slab)
  {
    var slabElement = model.Instances.New<IfcSlab>(s =>
    {
      s.Name = "OpenRebar Host Slab";
      s.PredefinedType = IfcSlabTypeEnum.FLOOR;
    });

    AttachSlabProperties(model, slabElement, slab);

    return slabElement;
  }

  private static void AttachSlabProperties(IfcStore model, IfcSlab slabElement, SlabGeometry slab)
  {
    var propertySet = model.Instances.New<IfcPropertySet>(pset =>
    {
      pset.Name = "Pset_SlabCommon";
      pset.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
        {
          p.Name = "ConcreteClass";
          p.NominalValue = new IfcLabel(slab.ConcreteClass);
        }));
      pset.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
        {
          p.Name = "Thickness";
          p.NominalValue = new IfcPositiveLengthMeasure(slab.ThicknessMm * MillimetersToMeters);
        }));
      pset.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
        {
          p.Name = "Cover";
          p.NominalValue = new IfcPositiveLengthMeasure(slab.CoverMm * MillimetersToMeters);
        }));
    });

    model.Instances.New<IfcRelDefinesByProperties>(rel =>
    {
      rel.RelatingPropertyDefinition = propertySet;
      rel.RelatedObjects.Add(slabElement);
    });

    var quantitySet = model.Instances.New<IfcElementQuantity>(qty =>
    {
      qty.Name = "Qto_SlabBaseQuantities";
      qty.Quantities.Add(model.Instances.New<IfcQuantityArea>(q =>
        {
          q.Name = "GrossArea";
          q.AreaValue = slab.OuterBoundary.CalculateArea() * MillimetersToMeters * MillimetersToMeters;
        }));
      qty.Quantities.Add(model.Instances.New<IfcQuantityLength>(q =>
        {
          q.Name = "Thickness";
          q.LengthValue = slab.ThicknessMm * MillimetersToMeters;
        }));
    });

    model.Instances.New<IfcRelDefinesByProperties>(rel =>
    {
      rel.RelatingPropertyDefinition = quantitySet;
      rel.RelatedObjects.Add(slabElement);
    });
  }

  private static void AttachBarProperties(IfcStore model, IfcReinforcingBar bar, ReinforcementZone zone, RebarSegment segment)
  {
    var propertySet = model.Instances.New<IfcPropertySet>(pset =>
    {
      pset.Name = "Pset_ReinforcingBarBendingsBECCommon";
      pset.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
        {
          p.Name = "NominalDiameter";
          p.NominalValue = new IfcPositiveLengthMeasure(segment.DiameterMm * MillimetersToMeters);
        }));
      pset.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
        {
          p.Name = "BarLength";
          p.NominalValue = new IfcPositiveLengthMeasure(segment.TotalLength * MillimetersToMeters);
        }));
      pset.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
        {
          p.Name = "SteelGrade";
          p.NominalValue = new IfcLabel(zone.Spec.SteelClass);
        }));
      pset.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
        {
          p.Name = "ZoneId";
          p.NominalValue = new IfcIdentifier(zone.Id);
        }));
    });

    model.Instances.New<IfcRelDefinesByProperties>(rel =>
    {
      rel.RelatingPropertyDefinition = propertySet;
      rel.RelatedObjects.Add(bar);
    });
  }

  private static void AttachBarQuantities(IfcStore model, IfcReinforcingBar bar, RebarSegment segment)
  {
    var quantitySet = model.Instances.New<IfcElementQuantity>(qty =>
    {
      qty.Name = "Qto_ReinforcingBarBaseQuantities";
      qty.Quantities.Add(model.Instances.New<IfcQuantityLength>(q =>
        {
          q.Name = "Length";
          q.LengthValue = segment.TotalLength * MillimetersToMeters;
        }));
      qty.Quantities.Add(model.Instances.New<IfcQuantityArea>(q =>
        {
          q.Name = "CrossSectionArea";
          q.AreaValue = Math.PI * Math.Pow(segment.DiameterMm * MillimetersToMeters / 2.0, 2);
        }));
    });

    model.Instances.New<IfcRelDefinesByProperties>(rel =>
    {
      rel.RelatingPropertyDefinition = quantitySet;
      rel.RelatedObjects.Add(bar);
    });

  }

  private static void AttachBarMaterial(
      IfcStore model,
      IfcReinforcingBar bar,
      string steelClass,
      IDictionary<string, IfcMaterial> materialCache)
  {
    if (!materialCache.TryGetValue(steelClass, out var material))
    {
      material = model.Instances.New<IfcMaterial>(m => m.Name = steelClass);
      materialCache[steelClass] = material;
    }

    model.Instances.New<IfcRelAssociatesMaterial>(rel =>
    {
      rel.RelatingMaterial = material;
      rel.RelatedObjects.Add(bar);
    });
  }

  private static void AttachBarType(
      IfcStore model,
      IfcReinforcingBar bar,
      ReinforcementZone zone,
      RebarSegment segment,
      IDictionary<string, IfcReinforcingBarType> typeCache)
  {
    string key = string.Create(CultureInfo.InvariantCulture, $"{segment.DiameterMm}|{zone.Spec.SteelClass}");
    if (!typeCache.TryGetValue(key, out var barType))
    {
      bool periodic = AnchorageRules.IsPeriodicProfile(zone.Spec.SteelClass);
      barType = model.Instances.New<IfcReinforcingBarType>(entity =>
      {
        entity.Name = key;
        entity.PredefinedType = IfcReinforcingBarTypeEnum.MAIN;
        entity.NominalDiameter = segment.DiameterMm * MillimetersToMeters;
        entity.BarSurface = periodic
            ? IfcReinforcingBarSurfaceEnum.TEXTURED
            : IfcReinforcingBarSurfaceEnum.PLAIN;
      });
      typeCache[key] = barType;
    }

    model.Instances.New<IfcRelDefinesByType>(rel =>
    {
      rel.RelatingType = barType;
      rel.RelatedObjects.Add(bar);
    });
  }

  private static void AttachBarShape(
      IfcStore model,
      IfcReinforcingBar bar,
      ReinforcementZone zone,
      RebarSegment segment,
      SlabGeometry slab,
      IfcGeometricRepresentationContext context)
  {
    double elevationMm = LayerElevations.AxisElevationMm(
        zone.Layer,
        zone.Direction,
        slab.ThicknessMm,
        slab.CoverMm,
        slab.CoverMm,
        segment.DiameterMm,
        segment.DiameterMm);
    if (!HookCenterline.TryBuild(segment, elevationMm, tailMm: 0, bendUp: zone.Layer != RebarLayer.Bottom, out var spans, out _))
      return;

    var origin = spans[0].Start;
    double dx = segment.End.X - segment.Start.X;
    double dy = segment.End.Y - segment.Start.Y;
    double length = Math.Sqrt(dx * dx + dy * dy);
    if (length < 1e-6)
      return;
    double ux = dx / length;
    double uy = dy / length;

    bar.ObjectPlacement = model.Instances.New<IfcLocalPlacement>(placement =>
    {
      placement.RelativePlacement = model.Instances.New<IfcAxis2Placement3D>(axis =>
      {
        axis.Location = Point(model, origin.X * MillimetersToMeters, origin.Y * MillimetersToMeters, origin.Z * MillimetersToMeters);
        axis.Axis = Direction(model, 0, 0, 1);
        axis.RefDirection = Direction(model, ux, uy, 0);
      });
    });

    IfcCurve directrix = spans.Any(span => span.Through is not null)
        ? Composite(model, spans, origin, ux, uy)
        : Polyline(model, spans.SelectMany(span => new[] { span.Start, span.End }), origin, ux, uy);

    var solid = model.Instances.New<IfcSweptDiskSolid>(entity =>
    {
      entity.Directrix = directrix;
      entity.Radius = segment.DiameterMm * MillimetersToMeters / 2.0;
    });
    var shape = model.Instances.New<IfcShapeRepresentation>(representation =>
    {
      representation.ContextOfItems = context;
      representation.RepresentationIdentifier = "Body";
      representation.RepresentationType = "AdvancedSweptSolid";
      representation.Items.Add(solid);
    });
    bar.Representation = model.Instances.New<IfcProductDefinitionShape>(definition =>
    {
      definition.Representations.Add(shape);
    });
  }

  private static IfcPolyline Polyline(
      IfcStore model,
      IEnumerable<HookCenterline.Point> points,
      HookCenterline.Point origin,
      double ux,
      double uy)
  {
    var curve = model.Instances.New<IfcPolyline>();
    HookCenterline.Point? previous = null;
    foreach (var point in points)
    {
      if (previous is { } prior && Same(prior, point))
        continue;
      curve.Points.Add(Point(model, Local(point, origin, ux, uy)));
      previous = point;
    }

    return curve;
  }

  private static IfcCompositeCurve Composite(
      IfcStore model,
      IReadOnlyList<HookCenterline.Span> spans,
      HookCenterline.Point origin,
      double ux,
      double uy)
  {
    var curve = model.Instances.New<IfcCompositeCurve>();
    foreach (var span in spans)
    {
      var points = span.Through is { } through
          ? SampleArc(span.Start, through, span.End)
          : new[] { span.Start, span.End };
      var parent = Polyline(model, points, origin, ux, uy);
      curve.Segments.Add(model.Instances.New<IfcCompositeCurveSegment>(segment =>
      {
        segment.Transition = IfcTransitionCode.CONTINUOUS;
        segment.SameSense = true;
        segment.ParentCurve = parent;
      }));
    }

    return curve;
  }

  private static IReadOnlyList<HookCenterline.Point> SampleArc(
      HookCenterline.Point start,
      HookCenterline.Point through,
      HookCenterline.Point end)
  {
    var samples = new List<HookCenterline.Point> { start, through, end };
    return samples;
  }

  private static (double X, double Y, double Z) Local(
      HookCenterline.Point point,
      HookCenterline.Point origin,
      double ux,
      double uy)
  {
    double dx = point.X - origin.X;
    double dy = point.Y - origin.Y;
    double dz = point.Z - origin.Z;
    return (
        (dx * ux + dy * uy) * MillimetersToMeters,
        (dx * -uy + dy * ux) * MillimetersToMeters,
        dz * MillimetersToMeters);
  }

  private static bool Same(HookCenterline.Point left, HookCenterline.Point right) =>
      Math.Abs(left.X - right.X) < 1e-6
      && Math.Abs(left.Y - right.Y) < 1e-6
      && Math.Abs(left.Z - right.Z) < 1e-6;

  private static IfcCartesianPoint Point(IfcStore model, double x, double y, double z)
  {
    var point = model.Instances.New<IfcCartesianPoint>();
    point.SetXYZ(x, y, z);
    return point;
  }

  private static IfcCartesianPoint Point(IfcStore model, (double X, double Y, double Z) value) =>
      Point(model, value.X, value.Y, value.Z);

  private static IfcDirection Direction(IfcStore model, double x, double y, double z)
  {
    var direction = model.Instances.New<IfcDirection>();
    direction.SetXYZ(x, y, z);
    return direction;
  }
}
