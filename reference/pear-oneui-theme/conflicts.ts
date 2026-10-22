/**
 * One UI 테마와 같은 곳(배경 · 상단 바 · 재생 화면 · 진행 바)을 덮어써서 충돌하는 플러그인들.
 * 테마가 켜져 있으면 자동으로 끄고, 상단 '확장' 메뉴에서도 숨긴다.
 */
export const ONEUI_CONFLICTS = [
  'album-color-theme', // 배경색 · 진행 바 색
  'blur-nav-bar', // 상단 바 배경
  'transparent-player', // 앱 배경 · 블러
  'ambient-mode', // 재생 화면 배경 빛 (무거움)
];
