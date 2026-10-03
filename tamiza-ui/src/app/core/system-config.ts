/** Public settings served by the API at `GET /api/v1/system/config`. */
export interface SystemConfig {
  oidc: {
    authority: string;
    clientId: string;
    scope: string;
  };
  version: string;
}

export const SYSTEM_CONFIG_URL = '/api/v1/system/config';
