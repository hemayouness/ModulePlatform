import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/** Admin projection of a module, including versions that are not active. */
export interface AdminModuleVersion {
  version: string;
  isActive: boolean;
  installedAt: string;
  sizeBytes: number;
  entryPath: string;
  contentHash: string;
}

export interface AdminModule {
  name: string;
  displayName: string;
  description?: string;
  isEnabled: boolean;
  activeVersion: string | null;
  versions: AdminModuleVersion[];
}

@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private readonly http = inject(HttpClient);

  list(): Promise<AdminModule[]> {
    return firstValueFrom(this.http.get<AdminModule[]>('/api/modules/admin'));
  }

  upload(file: File): Promise<AdminModule> {
    const form = new FormData();
    form.append('package', file, file.name);
    return firstValueFrom(this.http.post<AdminModule>('/api/modules', form));
  }

  activate(name: string, version: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/modules/${name}/${version}/activate`, {}),
    );
  }

  deactivate(name: string, version: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/modules/${name}/${version}/deactivate`, {}),
    );
  }

  enable(name: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/modules/${name}/enable`, {}));
  }

  disable(name: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/modules/${name}/disable`, {}));
  }

  uninstall(name: string, version: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`/api/modules/${name}/${version}`),
    );
  }
}
