// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

using FluentAssertions;

using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks.Admission;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission;

public sealed class AdmissionRequestModelBinderTest
{
    private const string ModelName = "request";
    private readonly AdmissionRequestModelBinder _binder = new();

    [Fact]
    public async Task Should_Bind_Valid_AdmissionRequest_With_ISODurationTimeSpan()
    {
        // Arrange
        const string expectedUid = "test-uid-123";
        const string expectedOperation = "CREATE";
        const string expectedValue = "some_value";

        const string json = $$"""
                            {
                              "apiVersion": "admission.k8s.io/v1",
                              "kind": "AdmissionReview",
                              "request": {
                                "uid": "{{expectedUid}}",
                                "operation": "{{expectedOperation}}",
                                "object": {
                                  "apiVersion": "test.kubeops.dev/v1",
                                  "kind": "TestEntity",
                                  "metadata": {
                                    "name": "test-entity",
                                    "namespace": "default"
                                  },
                                  "spec": {
                                    "value": "{{expectedValue}}",
                                    "timeout": "PT5M30S"
                                  }
                                },
                                "dryRun": true
                              }
                            }
                            """;

        var bindingContext = CreateBindingContext(
            json,
            typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>));

        // Act
        await _binder.BindModelAsync(bindingContext);

        // Assert
        bindingContext.Result.IsModelSet.Should().BeTrue();
        bindingContext.Result.Model.Should().BeOfType<AdmissionRequest<TestEntityWithISODurationTimeSpan>>();

        var result = (AdmissionRequest<TestEntityWithISODurationTimeSpan>)bindingContext.Result.Model;
        result.Request.Should().NotBeNull();
        result.Request.Uid.Should().Be(expectedUid);
        result.Request.Operation.Should().Be(expectedOperation);
        result.Request.Object.Should().NotBeNull();
        result.Request.Object.Metadata.Name.Should().Be("test-entity");
        result.Request.Object.Spec.Value.Should().Be(expectedValue);
        result.Request.Object.Spec.Timeout.Should().Be(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30)));
        result.Request.DryRun.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Bind_Valid_AdmissionRequest_With_TimeSpan()
    {
        // Arrange
        const string expectedUid = "test-uid-456";
        const string expectedOperation = "DELETE";
        const string expectedValue = "some_value";

        const string json = $$"""
                            {
                              "apiVersion": "admission.k8s.io/v1",
                              "kind": "AdmissionReview",
                              "request": {
                                "uid": "{{expectedUid}}",
                                "operation": "{{expectedOperation}}",
                                "object": {
                                  "apiVersion": "test.kubeops.dev/v1",
                                  "kind": "TestEntity",
                                  "metadata": {
                                    "name": "test-entity",
                                    "namespace": "default"
                                  },
                                  "spec": {
                                    "value": "{{expectedValue}}",
                                    "timeout": "05:30:00"
                                  }
                                },
                                "dryRun": true
                              }
                            }
                            """;

        var bindingContext = CreateBindingContext(
            json,
            typeof(AdmissionRequest<TestEntityWithTimeSpanConverter>));

        // Act
        await _binder.BindModelAsync(bindingContext);

        // Assert
        bindingContext.Result.IsModelSet.Should().BeTrue();
        bindingContext.Result.Model.Should().BeOfType<AdmissionRequest<TestEntityWithTimeSpanConverter>>();

        var result = (AdmissionRequest<TestEntityWithTimeSpanConverter>)bindingContext.Result.Model;
        result.Request.Should().NotBeNull();
        result.Request.Uid.Should().Be(expectedUid);
        result.Request.Operation.Should().Be(expectedOperation);
        result.Request.Object.Should().NotBeNull();
        result.Request.Object.Metadata.Name.Should().Be("test-entity");
        result.Request.Object.Spec.Value.Should().Be(expectedValue);
        result.Request.Object.Spec.Timeout.Should().Be(TimeSpan.FromHours(5).Add(TimeSpan.FromMinutes(30)));
        result.Request.DryRun.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Handle_Empty_Value()
    {
        // Arrange
        var bindingContext = CreateBindingContext(
            string.Empty,
            typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>));

        // Act
        await _binder.BindModelAsync(bindingContext);

        // Assert
        bindingContext.Result.IsModelSet.Should().BeFalse();
        bindingContext.Result.Model.Should().BeNull();
    }

    [Fact]
    public async Task Should_Handle_Missing_Value()
    {
        // Arrange
        var bindingContext = CreateBindingContext(
            null,
            typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>));

        // Act
        await _binder.BindModelAsync(bindingContext);

        // Assert
        bindingContext.Result.IsModelSet.Should().BeFalse();
        bindingContext.Result.Model.Should().BeNull();
    }

    private static DefaultModelBindingContext CreateBindingContext(string? json, Type modelType)
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new(),
            new());

        return new()
        {
            ActionContext = actionContext,
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType),
            ModelName = ModelName,
            ModelState = actionContext.ModelState,
            ValueProvider = new TestValueProvider(json),
        };
    }

    private sealed class TestValueProvider : IValueProvider
    {
        private readonly string? _value;

        public TestValueProvider(string? value)
        {
            _value = value;
        }

        public bool ContainsPrefix(string prefix) => prefix == ModelName;

        public ValueProviderResult GetValue(string key)
        {
            if (key == ModelName && !string.IsNullOrEmpty(_value))
            {
                return new(new(_value), CultureInfo.InvariantCulture);
            }

            return ValueProviderResult.None;
        }
    }
}
