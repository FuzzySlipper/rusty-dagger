import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ExperimentDocument,
  ExperimentEvaluation,
  ExperimentReadout,
} from './lab-contract';

const API_URL = '/api/dagger-lab';

@Injectable({ providedIn: 'root' })
export class LabApiService {
  private readonly http = inject(HttpClient);

  read(): Promise<ExperimentReadout> {
    return firstValueFrom(this.http.get<ExperimentReadout>(API_URL));
  }

  apply(document: ExperimentDocument): Promise<ExperimentReadout> {
    return firstValueFrom(
      this.http.put<ExperimentReadout>(`${API_URL}/experiment`, document),
    );
  }

  evaluate(document: ExperimentDocument): Promise<ExperimentEvaluation> {
    return firstValueFrom(
      this.http.post<ExperimentEvaluation>(`${API_URL}/evaluate`, document),
    );
  }

  reset(): Promise<ExperimentReadout> {
    return firstValueFrom(this.http.post<ExperimentReadout>(`${API_URL}/reset`, null));
  }

  play(): Promise<ExperimentReadout> {
    return firstValueFrom(this.http.post<ExperimentReadout>(`${API_URL}/play`, null));
  }

  jumpToContent(id: number): Promise<ExperimentReadout> {
    return firstValueFrom(
      this.http.post<ExperimentReadout>(`${API_URL}/content/jump`, { id }),
    );
  }
}
