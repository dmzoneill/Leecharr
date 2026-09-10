import React, { useState, useEffect } from "react";

export interface MediaArtworkImageProps {
  src?: string | null;
  alt?: string;
  aspectRatio?: string | number;
  width?: string | number;
  height?: string | number;
  className?: string;
  style?: React.CSSProperties;
  imageStyle?: React.CSSProperties;
  fallbackIcon?: React.ReactNode;
  fallbackText?: string;
  borderRadius?: string | number;
  onClick?: (e: React.MouseEvent<HTMLDivElement>) => void;
  loading?: "lazy" | "eager";
  objectFit?: "cover" | "contain" | "fill";
}

/**
 * Reusable MediaArtworkImage component that reserves aspect-ratio layout space
 * to prevent cumulative layout shift (CLS) and gracefully displays an icon/text
 * placeholder fallback when image URL is missing or fails to load.
 */
export const MediaArtworkImage: React.FC<MediaArtworkImageProps> = ({
  src,
  alt = "",
  aspectRatio,
  width,
  height,
  className = "",
  style = {},
  imageStyle = {},
  fallbackIcon = "🎬",
  fallbackText,
  borderRadius,
  onClick,
  loading = "lazy",
  objectFit = "cover",
}) => {
  const [hasError, setHasError] = useState(false);

  // Reset error state if image source changes
  useEffect(() => {
    setHasError(false);
  }, [src]);

  const hasValidSrc = Boolean(src && src.trim() && !hasError);

  const containerStyle: React.CSSProperties = {
    position: "relative",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    overflow: "hidden",
    flexShrink: 0,
    backgroundColor: "var(--bg-primary)",
    borderRadius: borderRadius ?? undefined,
    width: width ?? (aspectRatio && !height ? "100%" : undefined),
    height: height ?? undefined,
    aspectRatio: aspectRatio
      ? String(aspectRatio)
      : !width && !height
        ? "2 / 3"
        : undefined,
    cursor: onClick ? "pointer" : undefined,
    ...style,
  };

  return (
    <div
      className={`media-artwork-container ${className}`.trim()}
      style={containerStyle}
      onClick={onClick}
    >
      {hasValidSrc ? (
        <img
          src={src!}
          alt={alt}
          loading={loading}
          onError={() => setHasError(true)}
          style={{
            width: "100%",
            height: "100%",
            objectFit,
            borderRadius: borderRadius ?? undefined,
            display: "block",
            ...imageStyle,
          }}
        />
      ) : (
        <div
          className="media-artwork-fallback"
          style={{
            width: "100%",
            height: "100%",
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            padding: fallbackText ? "0.5rem" : "0.25rem",
            textAlign: "center",
            backgroundColor: "var(--bg-primary)",
            color: "var(--text-muted)",
            userSelect: "none",
          }}
        >
          <div
            style={{
              fontSize: fallbackText
                ? "2rem"
                : typeof width === "number" && width <= 40
                  ? "0.85rem"
                  : "1.5rem",
              lineHeight: 1,
              marginBottom: fallbackText ? "0.35rem" : 0,
            }}
          >
            {fallbackIcon}
          </div>
          {fallbackText && (
            <div
              style={{
                fontSize: "0.8rem",
                fontWeight: 600,
                color: "var(--text-secondary)",
                lineHeight: 1.25,
                wordBreak: "break-word",
                overflow: "hidden",
                textOverflow: "ellipsis",
                display: "-webkit-box",
                WebkitLineClamp: 2,
                WebkitBoxOrient: "vertical",
              }}
            >
              {fallbackText}
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default MediaArtworkImage;
