/** 색 모드 — system 이면 윈도우(OS)의 라이트/다크 설정을 따라간다 */
export type ColorMode = 'system' | 'light' | 'dark';

export type OneUiThemeConfig = {
  enabled: boolean;
  /** 라이트 / 다크 / 시스템 따라가기 */
  mode: ColorMode;
  /** 하단 재생 바를 화면에서 띄운 둥근 유리 카드로 그릴지 */
  floatingPlayer: boolean;
  /** 재생 바를 앨범 색 파형으로 그릴지 */
  waveSeekbar: boolean;
};

export const COLOR_MODES: ColorMode[] = ['system', 'light', 'dark'];
