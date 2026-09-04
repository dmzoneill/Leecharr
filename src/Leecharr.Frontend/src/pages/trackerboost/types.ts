export interface UnifiedDownloadItem {
  key: string;
  id?: number;
  infoHash: string;
  name: string;
  totalSize: number;
  ratio: number;
  seeders: number;
  isPrivate: boolean;
  sourceType: "real_client" | "leecharr";
  clientName: string;
}

export type TorrentMetaMap = Map<
  string,
  {
    posterUrl?: string | null;
    mediaTitle?: string | null;
    source?: string | null;
    year?: number | null;
    totalSize?: number;
  }
>;
