import { DOCUMENT, Injectable, inject, signal } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { Observable, filter, firstValueFrom, of, take } from 'rxjs';
import { clearConversations } from '../projects/assistant-storage';

export type AuthStatus = 'pending' | 'authenticated' | 'anonymous' | 'unavailable';
export type AuthUnavailableReason = 'insecure-context' | 'unreachable';

const RETURN_URL_KEY = 'tamiza.returnUrl';

/** Session state for the UI, on top of the OIDC client. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oidc = inject(OidcSecurityService);
  private readonly document = inject(DOCUMENT);
  private signingOut = false;

  readonly status = signal<AuthStatus>('pending');
  readonly unavailableReason = signal<AuthUnavailableReason | null>(null);
  /** The signed-in user's OIDC subject (`sub`), which keys what the browser tab keeps per user; null when unknown. */
  readonly subject = signal<string | null>(null);

  /** Runs before the first navigation: finishes a login callback and decides the session state. */
  async initialize(): Promise<void> {
    const window = this.document.defaultView;
    // PKCE needs WebCrypto, which browsers only expose in a secure context (HTTPS or localhost).
    if (!window?.isSecureContext || !window.crypto?.subtle) {
      this.markUnavailable('insecure-context');
      return;
    }

    try {
      // Fails fast when the API or the identity provider is unreachable, instead of a silent failure on sign-in.
      await firstValueFrom(this.oidc.preloadAuthWellKnownDocument());
      const result = await firstValueFrom(this.oidc.checkAuth());
      if (result.isAuthenticated) {
        this.status.set('authenticated');
        this.subject.set(subjectOf(result.userData));
        // A person who signs in after another one's session expired in this tab never inherits that conversation; the same person keeps theirs.
        clearConversations(this.storage(), this.subject());
        this.restoreReturnUrl();
        this.watchSession();
      } else if (result.errorMessage) {
        this.markUnavailable('unreachable');
      } else {
        this.status.set('anonymous');
      }
    } catch {
      this.markUnavailable('unreachable');
    }
  }

  /** Sends the user to the identity provider and remembers where to bring them back. */
  login(returnUrl = '/'): void {
    if (isSafeReturnUrl(returnUrl)) {
      this.document.defaultView?.sessionStorage.setItem(RETURN_URL_KEY, returnUrl);
    }
    this.oidc.authorize();
  }

  /** Ends the session in Tamiza and at the identity provider (RP-initiated logout). */
  logout(): void {
    this.signingOut = true;
    // Only the assistant's conversations: the OIDC library keeps its own state in the same store.
    clearConversations(this.storage());
    this.oidc.logoff().subscribe();
  }

  private storage(): Storage | null {
    try {
      return this.document.defaultView?.sessionStorage ?? null;
    } catch {
      return null;
    }
  }

  accessToken(): Observable<string> {
    return this.status() === 'authenticated' ? this.oidc.getAccessToken() : of('');
  }

  private markUnavailable(reason: AuthUnavailableReason): void {
    this.unavailableReason.set(reason);
    this.status.set('unavailable');
  }

  private restoreReturnUrl(): void {
    const window = this.document.defaultView;
    const returnUrl = window?.sessionStorage.getItem(RETURN_URL_KEY);
    window?.sessionStorage.removeItem(RETURN_URL_KEY);
    if (window && returnUrl && isSafeReturnUrl(returnUrl)) {
      // Rewrites the address before the router's first navigation, so it lands on the requested route.
      window.history.replaceState(window.history.state, '', returnUrl);
    }
  }

  /** When the session ends without a sign-out (refresh failed), sign in again and come back here. */
  private watchSession(): void {
    this.oidc.isAuthenticated$
      .pipe(
        filter((result) => !result.isAuthenticated && !this.signingOut),
        take(1),
      )
      .subscribe(() => {
        this.status.set('anonymous');
        const location = this.document.location;
        this.login(location.pathname + location.search);
      });
  }
}

/** The `sub` claim of the OIDC user data, if there is a usable one. */
function subjectOf(userData: unknown): string | null {
  const subject = (userData as { sub?: unknown } | null | undefined)?.sub;
  return typeof subject === 'string' && subject ? subject : null;
}

/** Only same-site paths; rejects protocol-relative URLs that would redirect elsewhere. */
export function isSafeReturnUrl(url: string): boolean {
  return url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/\\');
}
