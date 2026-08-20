import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ProductReadout } from './product-contract';
import { SpriteIndex } from './sprite-contract';

export interface SpriteManifestSaveResult {
  readonly status: string;
  readonly manifest: string;
  readonly projectDocs: string;
}

const API_URL = '/api/dagger-tools';

/** Optional inspection, debug, and content-editing operations.
 * Gameplay/product state and ordinary semantic actions do not belong here.
 */
@Injectable({ providedIn: 'root' })
export class LabToolsApiService {
  private readonly http = inject(HttpClient);

  jumpToContent(id: number): Promise<ProductReadout> {
    return firstValueFrom(
      this.http.post<ProductReadout>(`${API_URL}/content/jump`, { id }),
    );
  }

  grantItem(item: string, quantity: number): Promise<ProductReadout> {
    return firstValueFrom(
      this.http.post<ProductReadout>(`${API_URL}/inventory/grant`, { item, quantity }),
    );
  }

  spriteIndex(): Promise<SpriteIndex> {
    return firstValueFrom(this.http.get<SpriteIndex>(`${API_URL}/sprites/index`));
  }

  saveSpriteManifest(name: string, manifest: unknown): Promise<SpriteManifestSaveResult> {
    return firstValueFrom(
      this.http.post<SpriteManifestSaveResult>(`${API_URL}/sprites/manifest/${name}`, manifest),
    );
  }
}
