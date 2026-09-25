// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.ArrIntegration.Webhook;

public interface IArrWebhookRegistration
{
    bool Register(ArrConnectionDefinition connection);

    bool Unregister(ArrConnectionDefinition connection);

    bool RegisterWebhook(ArrConnectionDefinition connection);

    bool UnregisterWebhook(ArrConnectionDefinition connection);

    bool RegisterDownloadClient(ArrConnectionDefinition connection);

    bool UnregisterDownloadClient(ArrConnectionDefinition connection);
}
