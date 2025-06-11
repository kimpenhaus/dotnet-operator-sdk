using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace KubeOps.Transpiler.Test;

public sealed partial class CrdsMlcTest
{
    [Fact]
    public void Should_Set_Length_Information()
    {
        var crd = typeof(LengthAttrEntity).Transpile();

        var specProperties = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["property"];

        specProperties.MinLength.Should().Be(2);
        specProperties.MaxLength.Should().Be(42);
    }

    [Fact]
    public void Should_Set_MinLengthButNoMaxLength_Information()
    {
        var crd = typeof(JustMinLengthAttrEntity).Transpile();

        var specProperties = crd.Spec.Versions[0].Schema.OpenAPIV3Schema.Properties["property"];

        specProperties.MinLength.Should().Be(1);
        specProperties.MaxLength.Should().BeNull();
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public sealed class LengthAttrEntity : CustomKubernetesEntity
    {
        [Length(2, 42)]
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    public sealed class JustMinLengthAttrEntity : CustomKubernetesEntity
    {
        [Length(minLength: 1)]
        public string Property { get; set; } = null!;
    }
}
