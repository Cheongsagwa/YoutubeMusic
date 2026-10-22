import { t } from '@/i18n';
import { createPlugin } from '@/utils';

import { onMenu } from './menu';
import style from './style.css?inline';
import { SeekWave } from './wave';
import { startIcons } from './icons';
import { startScrollFade } from './scroll-fade';
import { startTooltips } from './tooltip';
import { startBackdropProxy } from './backdrop-proxy';
import { startMotion } from './motion';
import { startPlayerExtras } from './player-extras';
import { startBarIcons } from './bar-icons';
import { startImageFade } from './image-fade';
import { startDevBridge } from './debug-bridge';
import { ONEUI_CONFLICTS } from './conflicts';
import { installRendererDevBridge } from './debug-bridge-renderer';

import type { ColorMode, OneUiThemeConfig } from './types';

/**
 * One UI 테마 — CCBus 위젯 앱(Compose)의 One UI 디자인을 YouTube Music 화면에 입힌다.
 * 색·치수는 style.css 맨 위 토큰에 모여 있다 (버스앱 Tokens.kt · colors.xml 값을 옮긴 것).
 *
 * 모든 규칙은 <html class="oneui-theme"> 아래에서만 걸리므로
 * 플러그인을 끄면 클래스만 떼어 바로 원래 모습으로 돌아간다 (재시작 필요 없음).
 */
const ROOT_CLASS = 'oneui-theme';
const LIGHT_CLASS = 'oneui-light';
const DARK_CLASS = 'oneui-dark';
const FLOATING_CLASS = 'oneui-floating-player';

const defaultConfig: OneUiThemeConfig = {
  enabled: false,
  mode: 'system',
  floatingPlayer: true,
  waveSeekbar: true,
};

type RendererState = {
  styleSheet: CSSStyleSheet | null;
  media: MediaQueryList | null;
  mode: ColorMode;
  onSchemeChange: (() => void) | null;
  wave: SeekWave;
  stopIcons: (() => void) | null;
  stopFade: (() => void) | null;
  stopTips: (() => void) | null;
  stopProxy: (() => void) | null;
  stopMotion: (() => void) | null;
  stopExtras: (() => void) | null;
  stopBarIcons: (() => void) | null;
  stopImages: (() => void) | null;
  applyConfig: (config: OneUiThemeConfig) => void;
  applyScheme: () => void;
};

export default createPlugin({
  name: () => t('plugins.oneui-theme.name'),
  description: () => t('plugins.oneui-theme.description'),
  addedVersion: '3.12.x',
  restartNeeded: false,
  config: defaultConfig,
  menu: onMenu,
  // ⚠️ 개발 전용 디버그 통로 (debug-bridge.ts) — 작업이 끝나면 지운다
  backend: {
    stopBridge: null as (() => void) | null,
    async start(ctx) {
      this.stopBridge = startDevBridge(ctx);
      // 충돌하는 플러그인은 끈다 (conflicts.ts)
      // 설정 모듈은 메인 프로세스 전용이라, 화면 쪽 번들에서 실행되지 않도록 여기서만 불러온다
      const config = await import('@/config');
      for (const id of ONEUI_CONFLICTS) {
        if (await config.plugins.isEnabled(id)) config.plugins.disable(id);
      }
    },
    stop() {
      this.stopBridge?.();
    },
  },
  renderer: {
    styleSheet: null as CSSStyleSheet | null,
    media: null as MediaQueryList | null,
    mode: defaultConfig.mode as ColorMode,
    onSchemeChange: null as (() => void) | null,
    wave: new SeekWave(),
    stopIcons: null as (() => void) | null,
    stopFade: null as (() => void) | null,
    stopTips: null as (() => void) | null,
    stopProxy: null as (() => void) | null,
    stopMotion: null as (() => void) | null,
    stopExtras: null as (() => void) | null,
    stopBarIcons: null as (() => void) | null,
    stopImages: null as (() => void) | null,

    async start({ getConfig, ipc }) {
      const self = this as unknown as RendererState;
      if (import.meta.env.DEV) installRendererDevBridge(ipc);

      self.styleSheet = new CSSStyleSheet();
      await self.styleSheet.replace(style);
      document.adoptedStyleSheets = [
        ...document.adoptedStyleSheets,
        self.styleSheet,
      ];

      // 시스템 모드일 때 윈도우 테마가 바뀌면 바로 따라간다
      self.media = window.matchMedia('(prefers-color-scheme: light)');
      self.onSchemeChange = () => self.applyScheme();
      self.media.addEventListener('change', self.onSchemeChange);

      document.documentElement.classList.add(ROOT_CLASS);
      self.stopIcons = startIcons();
      self.stopFade = startScrollFade();
      self.stopTips = startTooltips();
      self.stopProxy = startBackdropProxy();
      self.stopMotion = startMotion();
      self.stopExtras = startPlayerExtras();
      self.stopBarIcons = startBarIcons();
      self.stopImages = startImageFade();
      self.applyConfig(await getConfig());
    },

    onPlayerApiReady(playerApi) {
      const self = this as unknown as RendererState;
      const thumbnailUrl = () =>
        playerApi.getPlayerResponse()?.videoDetails?.thumbnail?.thumbnails?.at(
          0,
        )?.url;

      // 지금 곡 색부터 잡고, 곡이 바뀔 때마다 다시 뽑는다
      void self.wave.updateColorFrom(thumbnailUrl());
      document.addEventListener('videodatachange', (event) => {
        if (event.detail.name !== 'dataloaded') return;
        void self.wave.updateColorFrom(thumbnailUrl());
      });
    },

    onConfigChange(newConfig) {
      (this as unknown as RendererState).applyConfig(newConfig);
    },

    stop() {
      const self = this as unknown as RendererState;
      if (self.media && self.onSchemeChange) {
        self.media.removeEventListener('change', self.onSchemeChange);
      }
      self.media = null;
      self.onSchemeChange = null;
      self.wave.detach();
      self.wave.clearColor();
      self.stopIcons?.();
      self.stopIcons = null;
      self.stopFade?.();
      self.stopFade = null;
      self.stopTips?.();
      self.stopTips = null;
      self.stopProxy?.();
      self.stopProxy = null;
      self.stopMotion?.();
      self.stopMotion = null;
      self.stopExtras?.();
      self.stopExtras = null;
      self.stopBarIcons?.();
      self.stopBarIcons = null;
      self.stopImages?.();
      self.stopImages = null;

      if (self.styleSheet) {
        const sheet = self.styleSheet;
        document.adoptedStyleSheets = document.adoptedStyleSheets.filter(
          (it) => it !== sheet,
        );
        self.styleSheet = null;
      }

      document.documentElement.classList.remove(
        ROOT_CLASS,
        LIGHT_CLASS,
        DARK_CLASS,
        FLOATING_CLASS,
      );
    },

    applyConfig(this: RendererState, config: OneUiThemeConfig) {
      this.mode = config.mode;
      document.documentElement.classList.toggle(
        FLOATING_CLASS,
        config.floatingPlayer,
      );
      if (config.waveSeekbar) this.wave.attach();
      else this.wave.detach();
      this.applyScheme();
    },

    applyScheme(this: RendererState) {
      const light =
        this.mode === 'light' ||
        (this.mode === 'system' && (this.media?.matches ?? false));
      const root = document.documentElement.classList;
      root.toggle(LIGHT_CLASS, light);
      root.toggle(DARK_CLASS, !light);
    },
  },
});
