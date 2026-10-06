// The rules live in public/rules.js so the panel and the API run exactly the same checks.
export {
  FORMAT,
  MAX_ANNOUNCEMENTS,
  MAX_DURATION_MS,
  MAX_FILE_BYTES,
  MAX_MESSAGE,
  MAX_TARGETS,
  MAX_TITLE,
  validateAnnouncement,
  validateFeed,
} from "../public/rules.js";

export type Severity = "Info" | "Warning" | "Critical";
export type Display = "Modal" | "Banner";

/** Same JSON as DashboardMetas.Core.Announcements.Announcement (camelCase). */
export interface Announcement {
  id: string;
  title: string;
  message: string;
  titleEn?: string;
  messageEn?: string;
  severity: Severity;
  display: Display;
  startsAt?: string;
  endsAt?: string;
  targets: string[];
  dismissible: boolean;
  sound: boolean;
}

export interface AnnouncementFeed {
  version: number;
  issuedAt: string;
  announcements: Announcement[];
}
