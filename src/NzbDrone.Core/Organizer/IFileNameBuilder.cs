// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Organizer;

public interface IFileNameBuilder
{
    string BuildFileName(EpisodeNamingContext context, string pattern = null, NamingConfig namingConfig = null);

    string BuildFileName(MovieNamingContext context, string pattern = null, NamingConfig namingConfig = null);

    string BuildSeriesDirectory(EpisodeNamingContext context, string pattern = null, NamingConfig namingConfig = null);

    string BuildSeasonDirectory(EpisodeNamingContext context, string pattern = null, NamingConfig namingConfig = null);

    string BuildMovieDirectory(MovieNamingContext context, string pattern = null, NamingConfig namingConfig = null);
}
