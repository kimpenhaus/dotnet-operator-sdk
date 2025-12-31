// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Operator.Web.Webhooks.Admission.Validation;

namespace KubeOps.Operator.Web.Test.TestApp;

[ValidationWebhook(typeof(TestEntityWithISODurationTimeSpan))]
public sealed class TestValidationWebhookForTestEntityWithISODurationTimeSpan : ValidationWebhook<TestEntityWithISODurationTimeSpan>
{
    public override ValidationResult Create(TestEntityWithISODurationTimeSpan entity, bool dryRun)
    {
        return Success();
    }

    public override ValidationResult Update(TestEntityWithISODurationTimeSpan oldEntity, TestEntityWithISODurationTimeSpan newEntity, bool dryRun)
    {
        return Success();
    }

    public override ValidationResult Delete(TestEntityWithISODurationTimeSpan entity, bool dryRun)
    {
        return Success();
    }
}
