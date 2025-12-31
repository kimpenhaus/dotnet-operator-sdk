// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;

using FluentAssertions;

using k8s;

using KubeOps.Operator.Serialization;
using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks.Admission;
using KubeOps.Operator.Web.Webhooks.Admission.Validation;

using Microsoft.AspNetCore.TestHost;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission.Validation;

public sealed class ValidationWebhookModelBindingTest
{
    [Fact]
    public async Task Should_Bind_CREATE_Request_With_TimeSpan_Correctly()
    {
        // Arrange
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var admissionRequest = new
        {
            apiVersion = "admission.k8s.io/v1",
            kind = "AdmissionReview",
            request = new
            {
                uid = "test-create-uid",
                operation = "CREATE",
                @object = new
                {
                    apiVersion = "test.kubeops.dev/v1",
                    kind = "TestEntity",
                    metadata = new { name = "test-entity", @namespace = "default" },
                    spec = new { value = "createvalue", timeout = "PT10M" },
                },
                dryRun = false,
            },
        };

        var json = KubernetesJsonSerializer.Serialize(admissionRequest, KubernetesJsonSerializer.SerializerOptions);

        // Act
        var response = await client.PostAsync(
            "/validate/testentitywithisodurationtimespan",
            new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AdmissionResponse>();
        result.Should().NotBeNull();
        result.Response.Uid.Should().Be("test-create-uid");
        result.Response.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Bind_UPDATE_Request_With_TimeSpan_Correctly()
    {
        // Arrange
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var admissionRequest = new
        {
            apiVersion = "admission.k8s.io/v1",
            kind = "AdmissionReview",
            request = new
            {
                uid = "test-update-uid",
                operation = "UPDATE",
                @object = new
                {
                    apiVersion = "test.kubeops.dev/v1",
                    kind = "TestEntity",
                    metadata = new { name = "test-entity", @namespace = "default" },
                    spec = new { value = "newvalue", timeout = "PT1H30M" },
                },
                oldObject = new
                {
                    apiVersion = "test.kubeops.dev/v1",
                    kind = "TestEntity",
                    metadata = new { name = "test-entity", @namespace = "default" },
                    spec = new { value = "oldvalue", timeout = "PT45M" },
                },
                dryRun = false,
            },
        };

        var json = KubernetesJson.Serialize(admissionRequest);

        // Act
        var response = await client.PostAsync(
            "/validate/testentitywithisodurationtimespan",
            new StringContent(json, Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AdmissionResponse>();
        result.Should().NotBeNull();
        result.Response.Uid.Should().Be("test-update-uid");
        result.Response.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Bind_DELETE_Request_With_TimeSpan_Correctly()
    {
        // Arrange
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var admissionRequest = new
        {
            apiVersion = "admission.k8s.io/v1",
            kind = "AdmissionReview",
            request = new
            {
                uid = "test-delete-uid",
                operation = "DELETE",
                oldObject = new
                {
                    apiVersion = "test.kubeops.dev/v1",
                    kind = "TestEntity",
                    metadata = new { name = "test-entity", @namespace = "default" },
                    spec = new { value = "deletedvalue", timeout = "PT20M" },
                },
                dryRun = false,
            },
        };

        var json = KubernetesJson.Serialize(admissionRequest);

        // Act
        var response = await client.PostAsync(
            "/validate/testentitywithisodurationtimespan",
            new StringContent(json, Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AdmissionResponse>();
        result.Should().NotBeNull();
        result.Response.Uid.Should().Be("test-delete-uid");
        result.Response.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Handle_DryRun_Flag()
    {
        // Arrange
        using var host = await TestHost.Create();
        var client = host.GetTestClient();

        var admissionRequest = new
        {
            apiVersion = "admission.k8s.io/v1",
            kind = "AdmissionReview",
            request = new
            {
                uid = "test-dryrun-uid",
                operation = "CREATE",
                @object = new
                {
                    apiVersion = "test.kubeops.dev/v1",
                    kind = "TestEntity",
                    metadata = new { name = "test-entity", @namespace = "default" },
                    spec = new { value = "dryrunvalue", timeout = "PT5M" },
                },
                dryRun = true,
            },
        };

        var json = KubernetesJson.Serialize(admissionRequest);

        // Act
        var response = await client.PostAsync(
            "/validate/testentitywithisodurationtimespan",
            new StringContent(json, Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ValidationResult>();
        result.Should().NotBeNull();
        result.Valid.Should().BeTrue();
    }
}
