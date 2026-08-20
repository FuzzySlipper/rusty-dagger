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

  equipItem(item: number): Promise<ProductReadout> {
    return firstValueFrom(this.http.post<ProductReadout>(`${API_URL}/equipment/equip`, { item }));
  }

  unequipSlot(slot: string): Promise<ProductReadout> {
    return firstValueFrom(this.http.post<ProductReadout>(`${API_URL}/equipment/unequip`, { slot }));
  }
}
