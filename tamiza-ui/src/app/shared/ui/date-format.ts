/**
 * The one format for a date with a time. It stops at the minute: seconds were the largest source of visual noise on the
 * synchronization log and the tables, and no screen needs them. A date without a time uses Angular's `mediumDate`.
 */
export const dateTimeFormat = 'MMM d, y, h:mm a';
