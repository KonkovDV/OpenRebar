using A101.Domain.Models;
using A101.Domain.Ports;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.IO;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MaterialResource;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.PropertyResource;
using Xbim.Ifc4.QuantityResource;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.Ifc4.StructuralElementsDomain;

namespace A101.Infrastructure.Export;

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
            ApplicationDevelopersName = "A101 Development",
            ApplicationFullName = "A101 Reinforcement",
            ApplicationIdentifier = "A101.Reinforcement",
            ApplicationVersion = "1.0.0",
            EditorsIdentifier = "a101@local",
            EditorsFamilyName = "Automation",
            EditorsGivenName = "A101",
            EditorsOrganisationIdentifier = "a101",
            EditorsOrganisationName = "A101 Development"
        };

        using var model = IfcStore.Create(editor, Xbim.Common.Step21.XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
        using var txn = model.BeginTransaction("A101 reinforcement export");

        var project = model.Instances.New<IfcProject>(p =>
        {
            p.Name = "A101 Reinforcement Export";
        });
        project.Initialize(ProjectUnits.SIUnitsUK);

        var site = model.Instances.New<IfcSite>(s =>
        {
            s.Name = "A101 Site";
            s.CompositionType = IfcElementCompositionEnum.ELEMENT;
        });
        var building = model.Instances.New<IfcBuilding>(b =>
        {
            b.Name = "A101 Building";
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

        foreach (var zone in zones)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var segment in zone.Rebars)
            {
                var bar = model.Instances.New<IfcReinforcingBar>(entity =>
                {
                    entity.Name = segment.Mark ?? $"BAR-{Guid.NewGuid():N}";
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
                AttachBarQuantities(model, bar, segment, zone);
            }
        }

        txn.Commit();
        model.SaveAs(outputPath);

        return Task.CompletedTask;
    }

    private static IfcSlab CreateSlab(IfcStore model, SlabGeometry slab)
    {
        var slabElement = model.Instances.New<IfcSlab>(s =>
        {
            s.Name = "A101 Host Slab";
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

    private static void AttachBarQuantities(IfcStore model, IfcReinforcingBar bar, RebarSegment segment, ReinforcementZone zone)
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

        var material = model.Instances.New<IfcMaterial>(m => m.Name = zone.Spec.SteelClass);
        model.Instances.New<IfcRelAssociatesMaterial>(rel =>
        {
            rel.RelatingMaterial = material;
            rel.RelatedObjects.Add(bar);
        });
    }
}