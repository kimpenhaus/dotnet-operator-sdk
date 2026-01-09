// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;

using FluentAssertions;

using k8s;

using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks.Admission;

using Microsoft.AspNetCore.TestHost;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission.Mutation;

public sealed class MutationWebhookModelBindingTest
{
    private static object CreateTestSpec(string value, string timeout)
        => new
        {
            apiVersion = "test.kubeops.dev/v1",
            kind = "TestEntity",
            metadata = new { name = "test-entity", @namespace = "default" },
            spec = new { value, timeout },
        };

    private static object CreateAdmissionReview(
        string uid,
        string operation,
        bool dryRun,
        object? @object = null,
        object? oldObject = null)
        => new
        {
            apiVersion = "admission.k8s.io/v1",
            kind = "AdmissionReview",
            request = new
            {
                uid,
                operation,
                dryRun,
                @object,
                oldObject,
            },
        };

    private static async Task<AdmissionResponse> PostMutationAsync(
        HttpClient client,
        string path,
        object admissionRequest)
    {
        var json = KubernetesJson.Serialize(admissionRequest);

        var response = await client.PostAsync(
            path,
            new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AdmissionResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();

        return result;
    }

    [Theory(DisplayName = "Mutation webhook binds CREATE request with ISO-8601 TimeSpan correctly")]
    [Trait("Category", "MutationWebhookModelBinding")]
    [InlineData("test-create-iso-uid", "createvalue", "PT10M")]
    [InlineData("test-create-iso-uid-long", "longvalue", "PT1H30M")]
    public async Task HandleAsync_CreateRequest_WithISODurationTimeSpanEntity_BindsAndMutatesCorrectly(
        string uid,
        string value,
        string timeout)
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var spec = CreateTestSpec(value, timeout);
        var admissionRequest = CreateAdmissionReview(
            uid: uid,
            operation: "CREATE",
            dryRun: false,
            @object: spec);

        var result = await PostMutationAsync(
            client,
            $"/mutate/{nameof(TestEntityWithISODurationTimeSpan).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be(uid);
        result.Response.Allowed.Should().BeTrue();
        result.Response.Patch.Should().NotBeNull("the mutation webhook should return a patch for modified entities");
    }

    [Fact(DisplayName = "Mutation webhook binds UPDATE request with ISO-8601 TimeSpan correctly")]
    [Trait("Category", "MutationWebhookModelBinding")]
    public async Task HandleAsync_UpdateRequest_WithISODurationTimeSpanEntity_BindsAndMutatesCorrectly()
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var @object = CreateTestSpec("newvalue", "PT1H30M");
        var oldObject = CreateTestSpec("oldvalue", "PT45M");
        var admissionRequest = CreateAdmissionReview(
            uid: "test-update-iso-uid",
            operation: "UPDATE",
            dryRun: false,
            @object: @object,
            oldObject: oldObject);

        var result = await PostMutationAsync(
            client,
            $"/mutate/{nameof(TestEntityWithISODurationTimeSpan).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be("test-update-iso-uid");
        result.Response.Allowed.Should().BeTrue();
        result.Response.Patch.Should().NotBeNull();
    }

    [Fact(DisplayName = "Mutation webhook binds DELETE request with ISO-8601 TimeSpan correctly")]
    [Trait("Category", "MutationWebhookModelBinding")]
    public async Task HandleAsync_DeleteRequest_WithISODurationTimeSpanEntity_BindsAndMutatesCorrectly()
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var oldObject = CreateTestSpec("deletedvalue", "PT20M");
        var admissionRequest = CreateAdmissionReview(
            uid: "test-delete-iso-uid",
            operation: "DELETE",
            dryRun: false,
            oldObject: oldObject);

        var result = await PostMutationAsync(
            client,
            $"/mutate/{nameof(TestEntityWithISODurationTimeSpan).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be("test-delete-iso-uid");
        result.Response.Allowed.Should().BeTrue();
        result.Response.Patch.Should().NotBeNull();
    }

    [Fact(DisplayName = "Mutation webhook respects dryRun flag for ISO-8601 entity")]
    [Trait("Category", "MutationWebhookModelBinding")]
    public async Task HandleAsync_CreateRequest_WithISODurationTimeSpanEntityAndDryRun_AllowsButDoesNotPersist()
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var spec = CreateTestSpec("dryrunvalue", "PT5M");
        var admissionRequest = CreateAdmissionReview(
            uid: "test-dryrun-iso-uid",
            operation: "CREATE",
            dryRun: true,
            @object: spec);

        var result = await PostMutationAsync(
            client,
            $"/mutate/{nameof(TestEntityWithISODurationTimeSpan).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Allowed.Should().BeTrue();
    }

    [Fact(DisplayName = "Mutation webhook applies mutations and returns patch for ISO-8601 entity")]
    [Trait("Category", "MutationWebhookModelBinding")]
    public async Task HandleAsync_CreateRequest_WithISODurationTimeSpanEntity_ReturnsPatchForMutatedEntity()
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var spec = CreateTestSpec("original", "PT10M");
        var admissionRequest = CreateAdmissionReview(
            uid: "test-mutation-iso-uid",
            operation: "CREATE",
            dryRun: false,
            @object: spec);

        var result = await PostMutationAsync(
            client,
            $"/mutate/{nameof(TestEntityWithISODurationTimeSpan).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be("test-mutation-iso-uid");
        result.Response.Allowed.Should().BeTrue();
        result.Response.Patch.Should().NotBeNull();
    }

    [Theory(DisplayName = "Mutation webhook binds CREATE request with custom TimeSpan converter correctly")]
    [Trait("Category", "MutationWebhookModelBinding")]
    [InlineData("test-create-converter-uid", "createvalue", "00:10:00")]
    [InlineData("test-create-converter-uid-long", "longvalue", "01:30:00")]
    public async Task HandleAsync_CreateRequest_WithTimeSpanConverterEntity_BindsAndMutatesCorrectly(
        string uid,
        string value,
        string timeout)
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var spec = CreateTestSpec(value, timeout);
        var admissionRequest = CreateAdmissionReview(
            uid: uid,
            operation: "CREATE",
            dryRun: false,
            @object: spec);

        var result = await PostMutationAsync(
            client,
            $"/mutate/{nameof(TestEntityWithTimeSpanConverter).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be(uid);
        result.Response.Allowed.Should().BeTrue();
        result.Response.Patch.Should().NotBeNull();
    }

    [Fact(DisplayName = "Mutation webhook binds UPDATE request with custom TimeSpan converter correctly")]
    [Trait("Category", "MutationWebhookModelBinding")]
    public async Task HandleAsync_UpdateRequest_WithTimeSpanConverterEntity_BindsAndMutatesCorrectly()
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var @object = CreateTestSpec("newvalue", "01:30:00");
        var oldObject = CreateTestSpec("oldvalue", "00:45:00");
        var admissionRequest = CreateAdmissionReview(
            uid: "test-update-converter-uid",
            operation: "UPDATE",
            dryRun: false,
            @object: @object,
            oldObject: oldObject);

        var result = await PostMutationAsync(
            client,
            $"/mutate/{nameof(TestEntityWithTimeSpanConverter).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be("test-update-converter-uid");
        result.Response.Allowed.Should().BeTrue();
        result.Response.Patch.Should().NotBeNull();
    }

    [Fact(DisplayName = "Mutation webhook binds DELETE request with custom TimeSpan converter correctly")]
    [Trait("Category", "MutationWebhookModelBinding")]
    public async Task HandleAsync_DeleteRequest_WithTimeSpanConverterEntity_BindsAndMutatesCorrectly()
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var oldObject = CreateTestSpec("deletedvalue", "00:20:00");
        var admissionRequest = CreateAdmissionReview(
            uid: "test-delete-converter-uid",
            operation: "DELETE",
            dryRun: false,
            oldObject: oldObject);

        var result = await PostMutationAsync(
            client,
            $"/mutate/{nameof(TestEntityWithTimeSpanConverter).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be("test-delete-converter-uid");
        result.Response.Allowed.Should().BeTrue();
        result.Response.Patch.Should().NotBeNull();
    }

    [Fact(DisplayName = "Mutation webhook respects dryRun flag for custom TimeSpan converter entity")]
    [Trait("Category", "MutationWebhookModelBinding")]
    public async Task HandleAsync_CreateRequest_WithTimeSpanConverterEntityAndDryRun_AllowsButDoesNotPersist()
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var spec = CreateTestSpec("dryrunvalue", "00:05:00");
        var admissionRequest = CreateAdmissionReview(
            uid: "test-dryrun-converter-uid",
            operation: "CREATE",
            dryRun: true,
            @object: spec);

        var result = await PostMutationAsync(
            client,
            $"/mutate/{nameof(TestEntityWithTimeSpanConverter).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Allowed.Should().BeTrue();
    }
}
