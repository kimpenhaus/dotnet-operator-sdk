// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using FluentAssertions;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Rbac;

namespace KubeOps.Transpiler.Test;

public sealed class RbacMlcTest(MlcProvider provider) : TranspilerTestBase(provider)
{
    [Fact]
    public void Should_Create_Generic_Policy()
    {
        var roles = _mlc
            .Transpile(_mlc.GetContextType<GenericRbacTest>().GetCustomAttributesData<GenericRbacAttribute>())
            .ToList();
        roles.Should().HaveCount(1);

        var role = roles[0];
        role.ApiGroups.Should().Contain("group");
        role.Resources.Should().Contain("configmaps");
        role.NonResourceURLs.Should().Contain("url");
        role.NonResourceURLs.Should().Contain("foobar");
        role.Verbs.Should().Contain(new[] { "get", "delete" });
    }

    [Fact]
    public void Should_Calculate_Max_Verbs_For_Types()
    {
        var roles = _mlc
            .Transpile(_mlc.GetContextType<RbacTest1>().GetCustomAttributesData<EntityRbacAttribute>())
            .ToList();
        roles.Should().HaveCount(1);

        var role = roles[0];
        role.Resources.Should().Contain("rbactest1s");
        role.Verbs.Should().Contain(new[] { "get", "update", "delete" });
    }

    [Fact]
    public void Should_Correctly_Calculate_All_Verb()
    {
        var roles = _mlc
            .Transpile(_mlc.GetContextType<RbacTest2>().GetCustomAttributesData<EntityRbacAttribute>())
            .ToList();
        roles.Should().HaveCount(1);

        var role = roles[0];
        role.Resources.Should().Contain("rbactest2s");
        role.Verbs.Should().Contain("*").And.HaveCount(1);
    }

    [Fact]
    public void Should_Group_Same_Types_Together()
    {
        var roles = _mlc
            .Transpile(_mlc.GetContextType<RbacTest3>().GetCustomAttributesData<EntityRbacAttribute>())
            .ToList();

        roles.Should().HaveCount(2);
        roles.Should()
            .Contain(rule => rule.Resources.Contains("rbactest1s"))
            .And
            .Contain(rule => rule.Resources.Contains("rbactest2s"));
    }

    [Fact]
    public void Should_Group_Types_With_Same_Verbs_Together()
    {
        var roles = _mlc
            .Transpile(_mlc.GetContextType<RbacTest4>().GetCustomAttributesData<EntityRbacAttribute>())
            .ToList();

        roles.Should().HaveCount(2);
        roles.Should()
            .Contain(rule => rule.Resources.Contains("rbactest1s") &&
                             rule.Resources.Contains("rbactest4s") &&
                             rule.Verbs.Contains("get") &&
                             rule.Verbs.Contains("update"))
            .And
            .Contain(rule => rule.Resources.Contains("rbactest2s") &&
                             rule.Resources.Contains("rbactest3s") &&
                             rule.Verbs.Contains("delete"));
    }

    [Fact]
    public void Should_Not_Mix_ApiGroups()
    {
        var roles = _mlc
            .Transpile(_mlc.GetContextType<RbacTest5>().GetCustomAttributesData<EntityRbacAttribute>())
            .ToList();

        roles.Should().HaveCount(5);
    }

    [Fact]
    public void Should_Correctly_Calculate_All_Verbs_Explicitly()
    {
        var role = _mlc
            .Transpile(_mlc.GetContextType<RbacTest6>().GetCustomAttributesData<EntityRbacAttribute>())
            .ToList()[0];

        role.Resources.Should().Contain("leases");
        role.Verbs.Should().Contain(new[] { "get", "list", "watch", "create", "update", "patch", "delete" });
    }

    [KubernetesEntity(Group = "test", ApiVersion = "v1")]
    [EntityRbac(typeof(RbacTest1), Verbs = RbacVerb.Get)]
    [EntityRbac(typeof(RbacTest1), Verbs = RbacVerb.Update)]
    [EntityRbac(typeof(RbacTest1), Verbs = RbacVerb.Delete)]
    private sealed class RbacTest1 : CustomKubernetesEntity;

    [KubernetesEntity(Group = "test", ApiVersion = "v1")]
    [EntityRbac(typeof(RbacTest2), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(RbacTest2), Verbs = RbacVerb.Delete)]
    private sealed class RbacTest2 : CustomKubernetesEntity;

    [KubernetesEntity(Group = "test", ApiVersion = "v1")]
    [EntityRbac(typeof(RbacTest1), Verbs = RbacVerb.Get)]
    [EntityRbac(typeof(RbacTest1), Verbs = RbacVerb.Update)]
    [EntityRbac(typeof(RbacTest2), Verbs = RbacVerb.Delete)]
    private sealed class RbacTest3 : CustomKubernetesEntity;

    [KubernetesEntity(Group = "test", ApiVersion = "v1")]
    [EntityRbac(typeof(RbacTest1), Verbs = RbacVerb.Get)]
    [EntityRbac(typeof(RbacTest1), Verbs = RbacVerb.Update)]
    [EntityRbac(typeof(RbacTest2), Verbs = RbacVerb.Delete)]
    [EntityRbac(typeof(RbacTest2), Verbs = RbacVerb.Delete)]
    [EntityRbac(typeof(RbacTest3), Verbs = RbacVerb.Delete)]
    [EntityRbac(typeof(RbacTest4), Verbs = RbacVerb.Get | RbacVerb.Update)]
    private sealed class RbacTest4 : CustomKubernetesEntity;

    [KubernetesEntity(Group = "test", ApiVersion = "v1")]
    [EntityRbac(typeof(V1Deployment), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Service), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Lease), Verbs = RbacVerb.All)]
    private sealed class RbacTest5 : CustomKubernetesEntity;

    [KubernetesEntity(Group = "test", ApiVersion = "v1")]
    [EntityRbac(typeof(V1Lease), Verbs = RbacVerb.AllExplicit)]
    private sealed class RbacTest6 : CustomKubernetesEntity;

    [KubernetesEntity(Group = "test", ApiVersion = "v1")]
    [GenericRbac(Urls = ["url", "foobar"], Resources = ["configmaps"], Groups = ["group"],
        Verbs = RbacVerb.Delete | RbacVerb.Get)]
    private sealed class GenericRbacTest : CustomKubernetesEntity;
}
