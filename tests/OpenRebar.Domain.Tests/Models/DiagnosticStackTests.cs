using FluentAssertions;
using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Tests.Models;

public class DiagnosticStackTests
{
  [Fact]
  public void Sha256_HashesTheText_AndDropsAnEmptyStack()
  {
    DiagnosticStack.Sha256(null).Should().BeNull();
    DiagnosticStack.Sha256("").Should().BeNull();
    DiagnosticStack.Sha256("abc").Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
  }
}
