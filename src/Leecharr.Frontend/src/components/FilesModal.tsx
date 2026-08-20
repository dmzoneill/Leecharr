import React, { useState, useEffect } from "react";
import { Torrent, TorrentFile } from "../api/types";
import { api } from "../api/client";

interface FilesModalProps {
  torrent: Torrent;
  onClose: () => void;
}

export const FilesModal: React.FC<FilesModalProps> = ({ torrent, onClose }) => {
  const [files, setFiles] = useState<TorrentFile[]>([]);
  const [loading, setLoading] = useState<boolean>(true);

  useEffect(() => {
    const loadFiles = async () => {
      try {
        const fileList = await api.getTorrentFiles(torrent.id);
        setFiles(fileList);
      } catch (err) {
        console.error("Failed to load files:", err);
      } finally {
        setLoading(false);
      }
    };

    loadFiles();
  }, [torrent.id]);

  const formatBytes = (bytes: number) => {
    if (!bytes) return "0 B";
    const k = 1024;
    const sizes = ["B", "KB", "MB", "GB", "TB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return `${(bytes / Math.pow(k, i)).toFixed(2)} ${sizes[i]}`;
  };

  const getPriorityLabel = (priority: number) => {
    switch (priority) {
      case 0:
        return "Do Not Download";
      case 1:
        return "Normal";
      case 2:
        return "High";
      case 7:
        return "Maximum";
      default:
        return "Normal";
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-content files-modal"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <h3>File Selection &mdash; {torrent.name}</h3>
          <button className="btn-close" onClick={onClose}>
            &times;
          </button>
        </div>
        <div className="modal-body">
          {loading ? (
            <p className="text-muted">Loading torrent files...</p>
          ) : files.length === 0 ? (
            <p className="text-muted">No files found for this torrent.</p>
          ) : (
            <div className="table-responsive">
              <table className="files-table">
                <thead>
                  <tr>
                    <th>File Path</th>
                    <th>Size</th>
                    <th>Progress</th>
                    <th>Priority</th>
                  </tr>
                </thead>
                <tbody>
                  {files.map((file) => (
                    <tr key={file.id}>
                      <td className="file-path">{file.path}</td>
                      <td>{formatBytes(file.size)}</td>
                      <td>
                        <div className="mini-progress-bar">
                          <div
                            className="mini-progress-fill"
                            style={{ width: `${(file.progress || 0) * 100}%` }}
                          ></div>
                        </div>
                        <span className="progress-text">
                          {Math.round((file.progress || 0) * 100)}%
                        </span>
                      </td>
                      <td>
                        <span
                          className={`priority-badge prio-${file.priority}`}
                        >
                          {getPriorityLabel(file.priority)}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
