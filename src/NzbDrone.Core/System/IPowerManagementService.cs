// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;

namespace NzbDrone.Core.SystemServices;

public enum PowerAction
{
    None = 0,
    Shutdown = 1,
    Suspend = 2,
    Hibernate = 3,
    ExitApplication = 4,
}

public interface IPowerManagementService
{
    Task<bool> ExecutePowerActionAsync(PowerAction action);

    bool IsInContainer { get; }
}
