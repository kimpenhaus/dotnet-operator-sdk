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
using KubeOps.Operator.Web.Webhooks.Admission.Validation;

using Microsoft.AspNetCore.TestHost;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission.Validation;

public sealed class ValidationWebhookModelBindingTest
{
    // Shared helpers to reduce duplication across tests while keeping them focused and readable.
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

    private static async Task<AdmissionResponse> PostAdmissionValidationAsync(
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

    private static async Task<ValidationResult> PostValidationResultAsync(
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

        var result = await response.Content.ReadFromJsonAsync<ValidationResult>(
            cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();

        return result;
    }

    [Theory(DisplayName = "Validation webhook binds CREATE request with ISO-8601 TimeSpan correctly")]
    [Trait("Category", "ValidationWebhookModelBinding")]
    [InlineData("test-create-iso-uid", "PT10M", "createvalue")]
    [InlineData("test-create-iso-uid-long", "PT1H30M", "othervalue")]
    public async Task HandleAsync_CreateRequest_WithISODurationTimeSpanEntity_BindsAndValidatesSuccessfully(
        string uid,
        string timeout,
        string value)
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var spec = CreateTestSpec(value, timeout);
        var admissionRequest = CreateAdmissionReview(
            uid: uid,
            operation: "CREATE",
            dryRun: false,
            @object: spec);

        var result = await PostAdmissionValidationAsync(
            client,
            $"/validate/{nameof(TestEntityWithISODurationTimeSpan).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be(uid);
        result.Response.Allowed.Should().BeTrue();
    }

    [Theory(DisplayName = "Validation webhook binds UPDATE request with ISO-8601 TimeSpan correctly")]
    [Trait("Category", "ValidationWebhookModelBinding")]
    [InlineData("test-update-iso-uid", "same", "same", true)]
    [InlineData("test-update-iso-uid-value-changed", "old", "new", false)]
    public async Task HandleAsync_UpdateRequest_WithISODurationTimeSpanEntity_BindsAndValidatesSuccessfully(
        string uid,
        string oldValue,
        string newValue,
        bool expectedAllowed)
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var @object = CreateTestSpec(newValue, "PT1H30M");
        var oldObject = CreateTestSpec(oldValue, "PT45M");
        var admissionRequest = CreateAdmissionReview(
            uid: uid,
            operation: "UPDATE",
            dryRun: false,
            @object: @object,
            oldObject: oldObject);

        var result = await PostAdmissionValidationAsync(
            client,
            $"/validate/{nameof(TestEntityWithISODurationTimeSpan).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be(uid);
        result.Response.Allowed.Should().Be(expectedAllowed);
    }

    [Theory(DisplayName = "Validation webhook binds DELETE request with ISO-8601 TimeSpan correctly")]
    [Trait("Category", "ValidationWebhookModelBinding")]
    [InlineData("test-delete-iso-uid", true)]
    public async Task HandleAsync_DeleteRequest_WithISODurationTimeSpanEntity_BindsAndValidatesSuccessfully(
        string uid,
        bool expectedAllowed)
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var oldObject = CreateTestSpec("deletedvalue", "PT20M");
        var admissionRequest = CreateAdmissionReview(
            uid: uid,
            operation: "DELETE",
            dryRun: false,
            oldObject: oldObject);

        var result = await PostAdmissionValidationAsync(
            client,
            $"/validate/{nameof(TestEntityWithISODurationTimeSpan).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be(uid);
        result.Response.Allowed.Should().Be(expectedAllowed);
    }

    [Fact(DisplayName = "Validation webhook respects dryRun flag for ISO-8601 entity")]
    [Trait("Category", "ValidationWebhookModelBinding")]
    public async Task HandleAsync_CreateRequest_WithISODurationTimeSpanEntityAndDryRun_BindsAndValidatesSuccessfully()
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var spec = CreateTestSpec("dryrunvalue", "PT5M");
        var admissionRequest = CreateAdmissionReview(
            uid: "test-dryrun-iso-uid",
            operation: "CREATE",
            dryRun: true,
            @object: spec);

        var result = await PostValidationResultAsync(
            client,
            $"/validate/{nameof(TestEntityWithISODurationTimeSpan).ToLowerInvariant()}",
            admissionRequest);

        result.Valid.Should().BeTrue();
    }

    [Theory(DisplayName = "Validation webhook binds CREATE request with custom TimeSpan converter correctly")]
    [Trait("Category", "ValidationWebhookModelBinding")]
    [InlineData("test-create-converter-uid", "00:10:00", "createvalue")]
    [InlineData("test-create-converter-uid-long", "01:30:00", "othervalue")]
    public async Task HandleAsync_CreateRequest_WithTimeSpanConverterEntity_BindsAndValidatesSuccessfully(
        string uid,
        string timeout,
        string value)
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var spec = CreateTestSpec(value, timeout);
        var admissionRequest = CreateAdmissionReview(
            uid: uid,
            operation: "CREATE",
            dryRun: false,
            @object: spec);

        var result = await PostAdmissionValidationAsync(
            client,
            $"/validate/{nameof(TestEntityWithTimeSpanConverter).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be(uid);
        result.Response.Allowed.Should().BeTrue();
    }

    [Theory(DisplayName = "Validation webhook binds UPDATE request with custom TimeSpan converter correctly")]
    [Trait("Category", "ValidationWebhookModelBinding")]
    [InlineData("test-update-converter-uid", 45, 60, true)]   // timeout increased
    [InlineData("test-update-converter-uid-short", 45, 30, false)] // timeout shortened
    public async Task HandleAsync_UpdateRequest_WithTimeSpanConverterEntity_BindsAndValidatesSuccessfully(
        string uid,
        int oldMinutes,
        int newMinutes,
        bool expectedAllowed)
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var @object = CreateTestSpec("newvalue", TimeSpan.FromMinutes(newMinutes).ToString("c"));
        var oldObject = CreateTestSpec("oldvalue", TimeSpan.FromMinutes(oldMinutes).ToString("c"));
        var admissionRequest = CreateAdmissionReview(
            uid: uid,
            operation: "UPDATE",
            dryRun: false,
            @object: @object,
            oldObject: oldObject);

        var result = await PostAdmissionValidationAsync(
            client,
            $"/validate/{nameof(TestEntityWithTimeSpanConverter).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be(uid);
        result.Response.Allowed.Should().Be(expectedAllowed);
    }

    [Theory(DisplayName = "Validation webhook binds DELETE request with custom TimeSpan converter correctly")]
    [Trait("Category", "ValidationWebhookModelBinding")]
    [InlineData("test-delete-converter-uid", true)]
    public async Task HandleAsync_DeleteRequest_WithTimeSpanConverterEntity_BindsAndValidatesSuccessfully(
        string uid,
        bool expectedAllowed)
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var oldObject = CreateTestSpec("deletedvalue", "00:20:00");
        var admissionRequest = CreateAdmissionReview(
            uid: uid,
            operation: "DELETE",
            dryRun: false,
            oldObject: oldObject);

        var result = await PostAdmissionValidationAsync(
            client,
            $"/validate/{nameof(TestEntityWithTimeSpanConverter).ToLowerInvariant()}",
            admissionRequest);

        result.Response.Uid.Should().Be(uid);
        result.Response.Allowed.Should().Be(expectedAllowed);
    }

    [Fact(DisplayName = "Validation webhook respects dryRun flag for TimeSpan converter entity")]
    [Trait("Category", "ValidationWebhookModelBinding")]
    public async Task HandleAsync_CreateRequest_WithTimeSpanConverterEntityAndDryRun_BindsAndValidatesSuccessfully()
    {
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var spec = CreateTestSpec("dryrunvalue", "00:10:00");
        var admissionRequest = CreateAdmissionReview(
            uid: "test-dryrun-converter-uid",
            operation: "CREATE",
            dryRun: true,
            @object: spec);

        var result = await PostValidationResultAsync(
            client,
            $"/validate/{nameof(TestEntityWithTimeSpanConverter).ToLowerInvariant()}",
            admissionRequest);

        result.Valid.Should().BeTrue();
    }
}
