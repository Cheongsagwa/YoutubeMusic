import { t } from '@/i18n';

import { COLOR_MODES, type OneUiThemeConfig } from './types';

import type { MenuTemplate } from '@/menu';
import type { MenuContext } from '@/types/contexts';

export const onMenu = async ({
  getConfig,
  setConfig,
}: MenuContext<OneUiThemeConfig>): Promise<MenuTemplate> => {
  const config = await getConfig();

  return [
    {
      label: t('plugins.oneui-theme.menu.mode.label'),
      submenu: COLOR_MODES.map((mode) => ({
        label: t(`plugins.oneui-theme.menu.mode.submenu.${mode}`),
        type: 'radio',
        checked: config.mode === mode,
        click() {
          setConfig({ mode });
        },
      })),
    },
    {
      label: t('plugins.oneui-theme.menu.floating-player'),
      type: 'checkbox',
      checked: config.floatingPlayer,
      click(item: Electron.MenuItem) {
        setConfig({ floatingPlayer: item.checked });
      },
    },
    {
      label: t('plugins.oneui-theme.menu.wave-seekbar'),
      type: 'checkbox',
      checked: config.waveSeekbar,
      click(item: Electron.MenuItem) {
        setConfig({ waveSeekbar: item.checked });
      },
    },
  ];
};
