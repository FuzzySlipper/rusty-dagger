import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LabReadout } from './lab-contract';
import { SpriteIndex } from './sprite-contract';

export interface SpriteManifestSaveResult {
  readonly status: string;
  readonly manifest: string;
  readonly projectDocs: string;
}

const API_URL = '/api/dagger-lab';

@Injectable({ providedIn: 'root' })
export class LabApiService {
  private readonly http = inject(HttpClient);

  read(): Promise<LabReadout> {
    return firstValueFrom(this.http.get<LabReadout>(API_URL));
  }

  reset(): Promise<LabReadout> {
    return firstValueFrom(this.http.post<LabReadout>(`${API_URL}/reset`, null));
  }

  play(): Promise<LabReadout> {
    return firstValueFrom(this.http.post<LabReadout>(`${API_URL}/play`, null));
  }

  jumpToContent(id: number): Promise<LabReadout> {
    return firstValueFrom(
      this.http.post<LabReadout>(`${API_URL}/content/jump`, { id }),
    );
  }

  equipItem(item: number): Promise<LabReadout> {
    return firstValueFrom(this.http.post<LabReadout>(`${API_URL}/equipment/equip`, { item }));
  }

  unequipSlot(slot: string): Promise<LabReadout> {
    return firstValueFrom(this.http.post<LabReadout>(`${API_URL}/equipment/unequip`, { slot }));
  }

  grantItem(item: string, quantity: number): Promise<LabReadout> {
    return firstValueFrom(this.http.post<LabReadout>(`${API_URL}/inventory/grant`, { item, quantity }));
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
