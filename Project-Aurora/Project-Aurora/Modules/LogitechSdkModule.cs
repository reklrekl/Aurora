﻿using System.Threading.Tasks;
using Aurora.Modules.Logitech;
using Lombok.NET;

namespace Aurora.Modules;

public sealed partial class LogitechSdkModule : AuroraModule
{
    public static LogitechSdkListener LogitechSdkListener { get; } = new();

    protected override async Task Initialize()
    {
        Global.logger.Information("Initializing Lightsync...");
        await LogitechSdkListener.Initialize();
        Global.logger.Information("Initialized Lightsync");
    }

    [Async]
    public override void Dispose()
    {
        LogitechSdkListener.Dispose();
    }
}