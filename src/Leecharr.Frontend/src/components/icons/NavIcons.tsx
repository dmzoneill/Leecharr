interface IconProps {
  size?: number;
  color?: string;
}

export function DashboardIcon({
  size = 16,
  color = "currentColor",
}: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect
        x="3"
        y="3"
        width="8"
        height="8"
        rx="1"
        stroke={color}
        strokeWidth="2"
      />
      <rect
        x="13"
        y="3"
        width="8"
        height="8"
        rx="1"
        stroke={color}
        strokeWidth="2"
      />
      <rect
        x="3"
        y="13"
        width="8"
        height="8"
        rx="1"
        stroke={color}
        strokeWidth="2"
      />
      <rect
        x="13"
        y="13"
        width="8"
        height="8"
        rx="1"
        stroke={color}
        strokeWidth="2"
      />
    </svg>
  );
}

export function TorrentIcon({ size = 16, color = "currentColor" }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path
        d="M12 3L12 15"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
      />
      <path
        d="M8 7L12 3L16 7"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M4 14V18C4 19.1 4.9 20 6 20H18C19.1 20 20 19.1 20 18V14"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
      />
    </svg>
  );
}

export function SettingsIcon({ size = 16, color = "currentColor" }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <circle cx="12" cy="12" r="3" stroke={color} strokeWidth="2" />
      <path
        d="M12 1V4M12 20V23M4.22 4.22L6.34 6.34M17.66 17.66L19.78 19.78M1 12H4M20 12H23M4.22 19.78L6.34 17.66M17.66 6.34L19.78 4.22"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
      />
    </svg>
  );
}

export function SystemIcon({ size = 16, color = "currentColor" }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect
        x="2"
        y="3"
        width="20"
        height="14"
        rx="2"
        stroke={color}
        strokeWidth="2"
      />
      <path d="M8 21H16" stroke={color} strokeWidth="2" strokeLinecap="round" />
      <path
        d="M12 17V21"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
      />
    </svg>
  );
}

export function SeedIcon({ size = 16, color = "currentColor" }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path
        d="M12 4C14.5 6.5 16 10 16 14C16 17.3 14.2 19 12 19C9.8 19 8 17.3 8 14C8 10 9.5 6.5 12 4Z"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M12 8V17"
        stroke={color}
        strokeWidth="1.5"
        strokeLinecap="round"
      />
      <path
        d="M12 12C10.5 13.5 10 15 10 16"
        stroke={color}
        strokeWidth="1.5"
        strokeLinecap="round"
        fill="none"
      />
      <path
        d="M12 12C13.5 13.5 14 15 14 16"
        stroke={color}
        strokeWidth="1.5"
        strokeLinecap="round"
        fill="none"
      />
    </svg>
  );
}

export function HealthIcon({ size = 16, color = "currentColor" }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path
        d="M22 12H18L15 21L9 3L6 12H2"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

export function BackupIcon({ size = 16, color = "currentColor" }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path
        d="M12 16L12 8"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
      />
      <path
        d="M9 11L12 8L15 11"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M20 16.58A5 5 0 0 0 18 7H17.74A8 8 0 1 0 4 15.25"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
      />
    </svg>
  );
}

export function NetworkIcon({ size = 16, color = "currentColor" }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <circle cx="12" cy="12" r="9" stroke={color} strokeWidth="2" />
      <path
        d="M12 3C14.5 5.5 16 8.5 16 12C16 15.5 14.5 18.5 12 21"
        stroke={color}
        strokeWidth="2"
      />
      <path
        d="M12 3C9.5 5.5 8 8.5 8 12C8 15.5 9.5 18.5 12 21"
        stroke={color}
        strokeWidth="2"
      />
      <path d="M3 12H21" stroke={color} strokeWidth="2" />
    </svg>
  );
}

export function TagIcon({ size = 16, color = "currentColor" }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path
        d="M12.586 2.586A2 2 0 0 0 11.172 2H4a2 2 0 0 0-2 2v7.172a2 2 0 0 0 .586 1.414l8 8a2 2 0 0 0 2.828 0l7.172-7.172a2 2 0 0 0 0-2.828l-8-8Z"
        stroke={color}
        strokeWidth="2"
      />
      <circle cx="7.5" cy="7.5" r="1.5" fill={color} />
    </svg>
  );
}

export function DownloadAgentIcon({
  size = 16,
  color = "currentColor",
}: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect
        x="2"
        y="4"
        width="20"
        height="16"
        rx="2"
        stroke={color}
        strokeWidth="2"
      />
      <path
        d="M12 8V14M12 14L9 11M12 14L15 11"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx="6" cy="16" r="1" fill={color} />
      <circle cx="18" cy="16" r="1" fill={color} />
    </svg>
  );
}
