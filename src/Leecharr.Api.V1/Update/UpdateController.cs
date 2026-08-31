// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using Leecharr.Http;
using Leecharr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;

namespace Leecharr.Api.V1.Update;

public class UpdateResource : RestResource
{
    public string Version { get; set; }

    public DateTime ReleaseDate { get; set; }

    public string FileName { get; set; }

    public string Url { get; set; }

    public bool Installed { get; set; }

    public bool Latest { get; set; }

    public List<string> Changes { get; set; } = new();
}

[V1ApiController("update")]
public class UpdateController : Controller
{
    [HttpGet]
    public ActionResult<List<UpdateResource>> GetUpdates()
    {
        var currentVersion = BuildInfo.Version?.ToString() ?? "1.0.0.0";
        var list = new List<UpdateResource>
        {
            new()
            {
                Id = 1,
                Version = currentVersion,
                ReleaseDate = DateTime.UtcNow,
                FileName = $"Leecharr.{currentVersion}.linux-x64.tar.gz",
                Url = "https://github.com/dmzoneill/Leecharr/releases",
                Installed = true,
                Latest = true,
                Changes = new List<string>
                {
                    "Multi-threaded BitTorrent download engine",
                    "Deep media correlation & pure C# EBML container inspection",
                    "Compatibility layers for qBittorrent, Deluge, Transmission",
                    "24x7 3-tier speed scheduling & multi-indexer search"
                }
            },
        };

        return this.Ok(list);
    }
}
