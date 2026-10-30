import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

/** What a client may read of a project at most: summaries only (`aggregates`) or summaries and rows. */
export type McpDataAccess = 'aggregates' | 'rows';

/** A project's access level for AI clients: nothing, summaries only, or summaries and rows. */
export type McpProjectAccess = 'off' | McpDataAccess;

/** The levels in the order the project's setting offers them. */
export const mcpProjectAccessLevels: McpProjectAccess[] = ['off', 'aggregates', 'rows'];

/** `GET /api/v1/mcp`: what a person needs to add Tamiza to an AI client. The API answers 404 when the server is off. */
export interface McpConnection {
  /** The MCP address, for example `https://tamiza.example.org/mcp`. */
  url: string;
  /** The public OAuth client registered in the identity provider for AI clients. */
  clientId: string;
  /** The loopback port registered for Claude Code. */
  callbackPort: number;
  /** The most the operator lets any project share. */
  maxDataAccess: McpDataAccess;
  /** The identity provider's account console, where a person revokes a connection. */
  accountUrl: string;
}

export type McpCallStatus = 'succeeded' | 'failed' | 'refused';

/** One call an AI client made on a project: who, which tool, how it ended and at what level. Never its arguments or results. */
export interface McpCall {
  id: string;
  startedAt: string;
  userName: string;
  tool: string;
  status: McpCallStatus;
  /** The refusal or failure code (`mcp.projectOff`, …); null for a call that succeeded. */
  errorCode: string | null;
  /** The level the call ran at; null for a call refused before the level was known. */
  access: McpDataAccess | null;
}

const base = '/api/v1';

@Injectable({ providedIn: 'root' })
export class McpService {
  private readonly http = inject(HttpClient);

  /** The address, client and callback port to connect with; fails with 404 when the server is not enabled. */
  connection(): Observable<McpConnection> {
    return this.http.get<McpConnection>(`${base}/mcp`);
  }

  /** The project's 50 most recent calls, newest first. Analysts and admins only: the API answers a viewer 403. */
  calls(projectId: string): Observable<McpCall[]> {
    return this.http.get<{ items: McpCall[] }>(`${base}/projects/${projectId}/mcp-calls`).pipe(map((list) => list.items));
  }
}
