import { provideHttpClient } from '@angular/common/http';
import { bootstrapApplication } from '@angular/platform-browser';
import { mountRustyApplication, type RustyApplicationHost } from '@rusty-engine/application-host';
import { AppComponent } from './app.component';
import { createDaggerDeveloperCommandClient } from './developer-command';
import {
  DAGGER_APPLICATION_CONTEXT,
  loadDaggerProductBootstrap,
  mountDaggerProductRuntime,
} from './product-runtime';

declare global {
  interface Window {
    __daggerApplicationHost?: RustyApplicationHost;
  }
}

const root = document.querySelector<HTMLElement>('#application');
if (root === null) throw new Error('Dagger application root is missing');
const bootstrap = await loadDaggerProductBootstrap();
const developerCommands = createDaggerDeveloperCommandClient();
const application = await mountRustyApplication({
  root,
  initialInteractionMode: 'gameplay',
  developerCommands: { client: developerCommands, label: 'Dagger developer commands' },
  renderer: { initialContent: bootstrap.content, clearColor: 0x080a0d },
  mountUi: async (uiRoot, context) => {
    const angularRoot = document.createElement('dagger-root');
    uiRoot.append(angularRoot);
    const angular = await bootstrapApplication(AppComponent, {
      providers: [provideHttpClient(), { provide: DAGGER_APPLICATION_CONTEXT, useValue: context }],
    });
    const runtime = mountDaggerProductRuntime(
      context.renderer,
      context,
      bootstrap.inputSequence,
    );
    return {
      dispose: () => {
        runtime.dispose();
        angular.destroy();
      },
    };
  },
});
application.renderer.setCameraPose(bootstrap.camera);
application.renderer.renderOnce();
window.__daggerApplicationHost = application;
document.body.dataset['daggerApplicationHost'] = 'ready';
document.body.dataset['daggerResourceCount'] = String(bootstrap.content.resources?.length ?? 0);
document.body.dataset['daggerSourceEntityCount'] = String(bootstrap.sourceEntityCount);
