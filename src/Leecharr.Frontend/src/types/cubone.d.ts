declare module "@cubone/react-file-manager" {
  import * as React from "react";

  export interface CuboneFile {
    name: string;
    isDirectory?: boolean;
    path: string;
    size?: number;
    updatedAt?: string;
    extension?: string;
  }

  export interface FileManagerPermissions {
    create?: boolean;
    upload?: boolean;
    move?: boolean;
    copy?: boolean;
    rename?: boolean;
    download?: boolean;
    delete?: boolean;
    [key: string]: boolean | undefined;
  }

  export interface FileUploadConfig {
    url: string;
    method?: string;
    headers?: Record<string, string>;
  }

  export type LayoutType = "grid" | "list";
  export type PasteOperationType = "copy" | "move";

  export interface FileManagerProps<TFile extends CuboneFile = CuboneFile> {
    files?: TFile[];
    currentPath?: string;
    initialPath?: string;
    primaryColor?: string;
    collapsibleNav?: boolean;
    defaultNavExpanded?: boolean;
    enableFilePreview?: boolean;
    height?: string | number;
    width?: string | number;
    className?: string;
    permissions?: FileManagerPermissions;
    fileUploadConfig?: FileUploadConfig;
    layout?: LayoutType;
    language?: string;
    onFolderChange?: (folder: string) => void;
    onFileOpen?: (file: TFile) => void;
    onFileSelect?: (file: TFile) => void;
    onSelectionChange?: (selectedFiles: TFile[]) => void;
    onRefresh?: () => void;
    onFileUploaded?: (response?: unknown) => void;
    onDelete?: (files: TFile[]) => void | Promise<void>;
    onRename?: (file: TFile, newName: string) => void | Promise<void>;
    onCreateFolder?: (
      nameOrParent?: string | TFile,
      parentFolder?: TFile,
    ) => void | Promise<void>;
    onCut?: (files: TFile[]) => void | Promise<void>;
    onCopy?: (files: TFile[]) => void | Promise<void>;
    onPaste?: (
      copiedFiles: TFile[],
      destinationFolder: TFile,
      operationType: PasteOperationType,
    ) => void | Promise<void>;
    onDownload?: (files: TFile[]) => void | Promise<void>;
    onMove?: (files: TFile[], targetFolder: TFile) => void | Promise<void>;
    onError?: (error: unknown) => void;
    onLayoutChange?: (layout: LayoutType) => void;
  }

  export const FileManager: <TFile extends CuboneFile = CuboneFile>(
    props: FileManagerProps<TFile>,
  ) => React.ReactElement | null;
}

declare module "@cubone/react-file-manager/dist/style.css" {
  const content: Record<string, string>;
  export default content;
}
