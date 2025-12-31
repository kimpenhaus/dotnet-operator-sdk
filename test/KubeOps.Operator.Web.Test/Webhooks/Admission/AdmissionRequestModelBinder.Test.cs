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
    public async Task Should_Bind_Valid_AdmissionRequest_With_TimeSpan_Property()
    {
        // Arrange
        const string json = """
                            {
                              "apiVersion": "admission.k8s.io/v1",
                              "kind": "AdmissionReview",
                              "request": {
                                "uid": "test-uid-123",
                                "operation": "CREATE",
                                "object": {
                                  "apiVersion": "test.kubeops.dev/v1",
                                  "kind": "TestEntity",
                                  "metadata": {
                                    "name": "test-entity",
                                    "namespace": "default"
                                  },
                                  "spec": {
                                    "value": "testvalue",
                                    "timeout": "PT5M30S"
                                  }
                                },
                                "dryRun": false
                              }
                            }
                            """;

        var bindingContext = CreateBindingContext(json, typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>));

        // Act
        await _binder.BindModelAsync(bindingContext);

        // Assert
        bindingContext.Result.IsModelSet.Should().BeTrue();

        var result = bindingContext.Result.Model as AdmissionRequest<TestEntityWithISODurationTimeSpan>;
        result.Should().NotBeNull();
        result.Request.Uid.Should().Be("test-uid-123");
        result.Request.Operation.Should().Be("CREATE");
        result.Request.Object.Should().NotBeNull();
        result.Request.Object!.Metadata.Name.Should().Be("test-entity");
        result.Request.Object.Spec.Value.Should().Be("testvalue");
        result.Request.Object.Spec.Timeout.Should().Be(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30)));
        result.Request.DryRun.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Bind_UPDATE_Operation_With_TimeSpan()
    {
        // Arrange
        const string json = """
                            {
                              "apiVersion": "admission.k8s.io/v1",
                              "kind": "AdmissionReview",
                              "request": {
                                "uid": "mutation-uid-456",
                                "operation": "UPDATE",
                                "object": {
                                  "apiVersion": "test.kubeops.dev/v1",
                                  "kind": "TestEntity",
                                  "metadata": {
                                    "name": "test-entity",
                                    "namespace": "default"
                                  },
                                  "spec": {
                                    "value": "newvalue",
                                    "timeout": "PT1H"
                                  }
                                },
                                "oldObject": {
                                  "apiVersion": "test.kubeops.dev/v1",
                                  "kind": "TestEntity",
                                  "metadata": {
                                    "name": "test-entity",
                                    "namespace": "default"
                                  },
                                  "spec": {
                                    "value": "oldvalue",
                                    "timeout": "PT30M"
                                  }
                                },
                                "dryRun": true
                              }
                            }
                            """;

        var bindingContext = CreateBindingContext(json, typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>));

        // Act
        await _binder.BindModelAsync(bindingContext);

        // Assert
        bindingContext.Result.IsModelSet.Should().BeTrue();

        var result = bindingContext.Result.Model as AdmissionRequest<TestEntityWithISODurationTimeSpan>;
        result.Should().NotBeNull();
        result.Request.Uid.Should().Be("mutation-uid-456");
        result.Request.Operation.Should().Be("UPDATE");
        result.Request.Object.Should().NotBeNull();
        result.Request.Object!.Spec.Value.Should().Be("newvalue");
        result.Request.Object.Spec.Timeout.Should().Be(TimeSpan.FromHours(1));
        result.Request.OldObject.Should().NotBeNull();
        result.Request.OldObject!.Spec.Value.Should().Be("oldvalue");
        result.Request.OldObject.Spec.Timeout.Should().Be(TimeSpan.FromMinutes(30));
        result.Request.DryRun.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Handle_Empty_Value()
    {
        // Arrange
        var bindingContext = CreateBindingContext(string.Empty, typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>));

        // Act
        await _binder.BindModelAsync(bindingContext);

        // Assert
        bindingContext.Result.IsModelSet.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Handle_Missing_Value()
    {
        // Arrange
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new(),
            new());

        var bindingContext = new DefaultModelBindingContext
        {
            ActionContext = actionContext,
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>)),
            ModelName = ModelName,
            ModelState = actionContext.ModelState,
            ValueProvider = new TestValueProvider(),
        };

        // Act
        await _binder.BindModelAsync(bindingContext);

        // Assert
        bindingContext.Result.IsModelSet.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Bind_DELETE_Operation()
    {
        // Arrange
        const string json = """
                            {
                              "apiVersion": "admission.k8s.io/v1",
                              "kind": "AdmissionReview",
                              "request": {
                                "uid": "delete-uid-789",
                                "operation": "DELETE",
                                "oldObject": {
                                  "apiVersion": "test.kubeops.dev/v1",
                                  "kind": "TestEntity",
                                  "metadata": {
                                    "name": "test-entity",
                                    "namespace": "default"
                                  },
                                  "spec": {
                                    "value": "deletedvalue",
                                    "timeout": "PT15M"
                                  }
                                },
                                "dryRun": false
                              }
                            }
                            """;

        var bindingContext = CreateBindingContext(json, typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>));

        // Act
        await _binder.BindModelAsync(bindingContext);

        // Assert
        bindingContext.Result.IsModelSet.Should().BeTrue();

        var result = bindingContext.Result.Model as AdmissionRequest<TestEntityWithISODurationTimeSpan>;
        result.Should().NotBeNull();
        result.Request.Uid.Should().Be("delete-uid-789");
        result.Request.Operation.Should().Be("DELETE");
        result.Request.Object.Should().BeNull();
        result.Request.OldObject.Should().NotBeNull();
        result.Request.OldObject!.Spec.Value.Should().Be("deletedvalue");
        result.Request.OldObject.Spec.Timeout.Should().Be(TimeSpan.FromMinutes(15));
    }

    private static DefaultModelBindingContext CreateBindingContext(string json, Type modelType)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(
            httpContext,
            new(),
            new());

        var valueProvider = new TestValueProvider(json);

        return new()
        {
            ActionContext = actionContext,
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType),
            ModelName = ModelName,
            ModelState = actionContext.ModelState,
            ValueProvider = valueProvider,
        };
    }

    private sealed class TestValueProvider : IValueProvider
    {
        private readonly string? _value;

        public TestValueProvider(string? value = null)
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
