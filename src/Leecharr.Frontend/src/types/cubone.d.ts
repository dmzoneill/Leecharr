declare module "@cubone/react-file-manager" {
  import * as React from "react";

  export interface FileManagerProps {
    files?: any[];
    currentPath?: string;
    onFolderChange?: (folder: any) => void;
    onFileOpen?: (file: any) => void;
    onFileSelect?: (file: any) => void;
    onSelectionChange?: (selectedFiles: any[]) => void;
    onRefresh?: () => void;
    onFileUploaded?: (response: any) => void;
    onDelete?: (files: any[]) => void;
    onRename?: (file: any, newName: string) => void;
    onCreateFolder?: (name: string, parentFolder?: any) => void | Promise<void>;
    onCut?: (files: any[]) => void;
    onCopy?: (files: any[]) => void;
    onPaste?: (
      filesOrFolder: any,
      destOrFiles: any,
      op?: any,
    ) => void | Promise<void>;
    onDownload?: (files: any[]) => void;
    onMove?: (files: any[], targetFolder: any) => void;
    onError?: (error: any) => void;
    fileUploadConfig?: any;
    layout?: "grid" | "list";
    onLayoutChange?: (layout: "grid" | "list") => void;
    enableFilePreview?: boolean;
    language?: string;
    [key: string]: any;
  }

  export const FileManager: React.FC<FileManagerProps>;
}
