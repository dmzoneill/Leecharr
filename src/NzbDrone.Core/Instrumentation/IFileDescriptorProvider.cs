// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Instrumentation;

public interface IFileDescriptorProvider
{
    int GetOpenFileDescriptorCount();

    int GetMaxFileDescriptors();

    double? GetFileDescriptorUsagePercentage();
}
