import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';

/** Public settings served by the API at `GET /api/v1/system/config`. */
export interface SystemConfig {
  oidc: {
    authority: string;
    clientId: string;
    scope: string;
  };
  version: string;
  /** Whether the deployment configured the AI script assistant; nothing else about it is public. */
  assistant: {
    enabled: boolean;
  };
  /** Whether the identity provider can send account emails (invitations, resends, password resets); nothing else about it is public. */
  accountEmails: {
    enabled: boolean;
  };
  /** Whether the deployment configured an SMTP server, so Tamiza sends email itself (scheduled dashboard emails); nothing else about it is public. */
  email: {
    enabled: boolean;
  };
  /** Whether the deployment turned on the MCP server for AI clients; its address and client are read after sign-in from `GET /api/v1/mcp`. */
  mcp: {
    enabled: boolean;
  };
}

export const SYSTEM_CONFIG_URL = '/api/v1/system/config';

/**
 * The deployment's public settings that pages need after sign-in. The configuration is read once, when the service is first
 * used; until it arrives, and when the request fails, every optional feature counts as off.
 */
@Injectable({ providedIn: 'root' })
export class SystemConfigService {
  private readonly http = inject(HttpClient);
  private readonly config = signal<SystemConfig | null>(null);

  /** Whether the AI script assistant is configured. False while the configuration loads and when it cannot be read. */
  readonly assistantEnabled = computed(() => this.config()?.assistant?.enabled === true);

  /** Whether account emails are configured. False while the configuration loads, when it cannot be read, and for an older API. */
  readonly accountEmailsEnabled = computed(() => this.config()?.accountEmails?.enabled === true);

  /** Whether Tamiza sends email itself. False while the configuration loads, when it cannot be read, and for an older API. */
  readonly emailEnabled = computed(() => this.config()?.email?.enabled === true);

  /** Whether the MCP server for AI clients is on. False while the configuration loads, when it cannot be read, and for an older API. */
  readonly mcpEnabled = computed(() => this.config()?.mcp?.enabled === true);

  constructor() {
    this.http.get<SystemConfig>(SYSTEM_CONFIG_URL).subscribe({
      next: (config) => this.config.set(config),
      error: () => this.config.set(null),
    });
  }
}
