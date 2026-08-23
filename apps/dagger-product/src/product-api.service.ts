import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ProductReadout } from './product-contract';

const API_URL = '/api/dagger-product';

@Injectable({ providedIn: 'root' })
export class ProductApiService {
  private readonly http = inject(HttpClient);

  read(): Promise<ProductReadout> {
    return firstValueFrom(this.http.get<ProductReadout>(`${API_URL}/readout`));
  }

  reset(): Promise<ProductReadout> {
    return firstValueFrom(this.http.post<ProductReadout>(`${API_URL}/session/reset`, null));
  }

  equipItem(item: number, slot: string, expectedEquipmentRevision: number): Promise<ProductReadout> {
    return firstValueFrom(this.http.post<ProductReadout>(`${API_URL}/equipment/equip`, {
      item, slot, expectedEquipmentRevision,
    }));
  }

  unequipSlot(slot: string, expectedItem: number, expectedEquipmentRevision: number): Promise<ProductReadout> {
    return firstValueFrom(this.http.post<ProductReadout>(`${API_URL}/equipment/unequip`, {
      slot, expectedItem, expectedEquipmentRevision,
    }));
  }

  moveInventoryGrid(sourceSlot: number, targetSlot: number, expectedRevision: number): Promise<ProductReadout> {
    return firstValueFrom(this.http.post<ProductReadout>(`${API_URL}/inventory/grid/move`, {
      sourceSlot, targetSlot, expectedRevision,
    }));
  }

  openAimedLoot(): Promise<ProductReadout> {
    return firstValueFrom(this.http.post<ProductReadout>(`${API_URL}/loot/open-aimed`, null));
  }

  transferLootStack(containerId: string, expectedInventoryRevision: number, item: string, quantity = 1): Promise<ProductReadout> {
    return firstValueFrom(this.http.post<ProductReadout>(`${API_URL}/loot/transfer-stack`, {
      containerId, expectedInventoryRevision, item, quantity,
    }));
  }

  transferLootItem(containerId: string, expectedInventoryRevision: number, item: number): Promise<ProductReadout> {
    return firstValueFrom(this.http.post<ProductReadout>(`${API_URL}/loot/transfer-item`, {
      containerId, expectedInventoryRevision, item,
    }));
  }

  closeLoot(): Promise<ProductReadout> {
    return firstValueFrom(this.http.post<ProductReadout>(`${API_URL}/loot/close`, null));
  }
}
