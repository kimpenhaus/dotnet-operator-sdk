using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace KubeOps.Transpiler.Test;

public sealed partial class CrdsMlcTest
{
    [Fact]
    public void Should_Add_GenericAdditionalPrinterColumns()
    {
        var crd = typeof(GenericAdditionalPrinterColumnAttrEntity).Transpile();
        var apc = crd.Spec.Versions[0].AdditionalPrinterColumns;

        apc.Should().NotBeNull();
        apc.Should().ContainSingle(def => def.JsonPath == ".metadata.namespace" && def.Name == "Namespace");
    }

    [Fact]
    public void Should_Add_InheritedGenericAdditionalPrinterColumns()
    {
        var crd = typeof(InheritedGenericAdditionalPrinterColumnAttrEntity).Transpile();
        var apc = crd.Spec.Versions[0].AdditionalPrinterColumns;

        apc.Should().NotBeNull();
        apc.Should().ContainSingle(def => def.JsonPath == ".metadata.namespace" && def.Name == "Namespace");
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [GenericAdditionalPrinterColumn(".metadata.namespace", "Namespace", "string")]
    public sealed class GenericAdditionalPrinterColumnAttrEntity : CustomKubernetesEntity
    {
        public string Property { get; set; } = null!;
    }

    [KubernetesEntity(Group = "testing.dev", ApiVersion = "v1", Kind = "TestEntity")]
    [InheritedGenericPrinterColumn]
    public class InheritedGenericAdditionalPrinterColumnAttrEntity : CustomKubernetesEntity
    {
        public string Property { get; set; } = null!;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class InheritedGenericPrinterColumnAttribute : GenericAdditionalPrinterColumnAttribute
    {
        public InheritedGenericPrinterColumnAttribute()
            : base(".metadata.namespace", "Namespace", "string")
        {
        }
    }
}
