# Leecharr Domain Model

## Entity Relationship Diagram

```mermaid
erDiagram
    Torrent {
        int Id PK
        string InfoHash UK
        string Name
        long TotalSize
        int PieceLength
        int PieceCount
        string Comment
        string CreatedBy
        datetime CreationDate
        bool IsPrivate
        string Status
        long Downloaded
        long Uploaded
        float Ratio
        float Progress
        long DownloadSpeed
        long UploadSpeed
        long Eta
        int Seeders
        int Leechers
        string SavePath
        string Category
        int Priority
        int DownloadLimit
        int UploadLimit
        bool SequentialDownload
        float TargetRatio
        int TargetSeedTimeMinutes
        datetime DateAdded
        datetime DateCompleted
        datetime LastActive
    }

    TorrentFile {
        int Id PK
        int TorrentId FK
        string Path
        long Size
        int PieceOffset
        int PieceCount
        int Priority
        float Progress
    }

    TorrentMediaMetadata {
        int Id PK
        int TorrentId FK
        string ArrType
        int ArrMediaId
        string Title
        int Year
        string Overview
        string PosterUrl
        string PosterLocalPath
        string BackdropUrl
        string BackdropLocalPath
        string MediaInfoJson
        string Genres
        float Rating
        string ImdbId
        string TmdbId
        string TvdbId
    }

    TrackerEntry {
        int Id PK
        int TorrentId FK
        string Url
        int Tier
        string Status
        bool Enabled
        int Seeders
        int Leechers
        int Downloaded
        int TotalAnnounces
        int SuccessfulAnnounces
        int ConsecutiveFailures
        long LastResponseTime
        int AnnounceInterval
        datetime LastAnnounce
        datetime NextAnnounce
        string ErrorMessage
    }

    ArrConnectionDefinition {
        int Id PK
        string Name
        string Implementation
        string ConfigContract
        string Settings
        bool Enable
        string Url
        string ApiKey
        string ArrType
        int SyncIntervalMinutes
        bool SyncEnabled
        bool AutoEnrichMetadata
        bool SyncCategories
    }

    SpeedSchedule {
        int Id PK
        string Name
        int Days
        string StartTime
        string EndTime
        int MaxUploadSpeed
        int MaxDownloadSpeed
        bool IsEnabled
        int Priority
    }

    Tag {
        int Id PK
        string Label
    }

    Config {
        int Id PK
        string Key
        string Value
    }

    ScheduledTask {
        int Id PK
        string TypeName
        int Interval
        datetime LastExecution
        datetime LastStartTime
    }

    CommandModel {
        int Id PK
        string Name
        string Body
        string Status
        datetime QueuedAt
        datetime StartedAt
        datetime EndedAt
        string Message
        int Priority
        string Trigger
    }

    Torrent ||--o{ TorrentFile : "contains files"
    Torrent ||--o| TorrentMediaMetadata : "enriched by"
    Torrent ||--o{ TrackerEntry : "announces to"
    Torrent }o--o{ Tag : "tagged with"
```

## Torrent Status Lifecycle

```mermaid
stateDiagram-v2
    [*] --> Queued: Add .torrent / Magnet / *arr Push
    Queued --> Checking: Verify existing pieces
    Checking --> Downloading: Start download
    Downloading --> Paused: Pause command
    Paused --> Downloading: Resume command
    Downloading --> Seeding: 100% downloaded
    Seeding --> Stopped: Ratio / Time limit reached or manual stop
    Stopped --> Downloading: Resume command
    Downloading --> Error: Disk full or I/O error
    Error --> Downloading: Retry / clear error
    Stopped --> [*]: Remove command
```

## Status Values

| Status        | Description                                                   |
| :------------ | :------------------------------------------------------------ |
| `Queued`      | Waiting in download queue for available slots                 |
| `Checking`    | Verifying piece hashes against disk files                     |
| `Downloading` | Actively downloading pieces from swarm                        |
| `Seeding`     | 100% complete, actively serving upload pieces to peers        |
| `Paused`      | Download or seeding paused by user                            |
| `Stopped`     | Completed target ratio/time or stopped by user                |
| `Error`       | Encountered disk I/O, storage full, or critical network error |
