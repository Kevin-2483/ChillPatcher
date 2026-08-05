import 'dart:io';

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:desktop_multi_window/desktop_multi_window.dart';
import 'package:window_manager/window_manager.dart';
import 'package:windows_single_instance/windows_single_instance.dart';
import 'floating/floating_player_window.dart';
import 'providers/app_state.dart';
import 'providers/core/app_state_bridge.dart';
import 'services/tray_manager.dart';
import 'services/port_file.dart';
import 'services/mod_deployment_service.dart';
import 'app.dart';

void main(List<String> args) async {
  WidgetsFlutterBinding.ensureInitialized();

  if (!kIsWeb) {
    HttpOverrides.global = UnixSocketHttpOverrides(PortFile.resolveSocketPath());
  }

  final currentWindow = await WindowController.fromCurrentEngine();
  final isFloating = _isFloatingPlayerWindow(currentWindow.arguments);

  if (isFloating) {
    runApp(
      FloatingPlayerWindowApp(
        controller: currentWindow,
        initialSnapshot: floatingPlayerSnapshotFromArguments(
          currentWindow.arguments,
        ),
      ),
    );
    return;
  }

  if (Platform.isWindows) {
    await WindowsSingleInstance.ensureSingleInstance(
      args,
      'OmniMixPlayerGUI',
      onSecondWindow: _onNewInstance,
      bringWindowToFront: true,
    );
  }

  await windowManager.ensureInitialized();

  // Read IPC port from port file (written by backend)
  final port = PortFile.readPort();

  await windowManager.setTitle('OmniMixPlayer');
  await windowManager.setMinimumSize(const Size(400, 500));
  await windowManager.setSize(const Size(900, 650));
  await windowManager.center();
  await windowManager.show();

  // Pre-load latest mod version from assets or playerbuild/ version_info.json
  await ModDeploymentService.loadLatestModVersion();

  final state = AppState();
  state.init(port: port);

  // 拦截原生 WM_CLOSE（X 按钮），WidgetsBindingObserver 无法可靠拦截
  final closeHandler = _CloseHandler(state);
  await windowManager.setPreventClose(true);
  windowManager.addListener(closeHandler);

  // System tray (desktop only)
  if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
    final tray = TrayManager();
    // Detect system language for tray menu labels (before Flutter app is ready)
    final isZh = Platform.localeName.startsWith('zh');
    final showHideLabel = isZh ? '显示/隐藏窗口' : 'Show/Hide Window';
    final exitGuiLabel = isZh ? '退出 GUI' : 'Exit GUI';
    final exitLabel = isZh ? '完全退出' : 'Fully Exit';

    await tray.init(
      showHideLabel: showHideLabel,
      exitGuiLabel: exitGuiLabel,
      exitLabel: exitLabel,
      onShowHide: () async {
        final isVisible = await windowManager.isVisible();
        if (isVisible) {
          await windowManager.hide();
        } else {
          await windowManager.show();
          await windowManager.focus();
        }
      },
      onExitGui: () async {
        await tray.dispose();
        exit(0);
      },
      onExit: () async {
        await state.fullQuit();
        await tray.dispose();
        exit(0);
      },
    );
  }

  runApp(
    ProviderScope(
      overrides: [appStateProvider.overrideWith((ref) => state)],
      child: const OmniMixApp(),
    ),
  );
}

bool _isFloatingPlayerWindow(dynamic arguments) {
  if (arguments == null) return false;
  if (arguments is! String) return false;
  if (arguments.isEmpty) return false;
  return arguments.contains('"type":"player_rectangle"') ||
      arguments.contains('"type": "player_rectangle"');
}

void _onNewInstance(List<String> args) {
  windowManager.show();
  windowManager.focus();
}

/// 拦截原生窗口关闭按钮，根据 closeBehavior 决定隐藏到托盘还是退出
class _CloseHandler with WindowListener {
  final AppState state;

  _CloseHandler(this.state);

  @override
  void onWindowClose() async {
    if (state.closeBehavior == 'minimize') {
      await windowManager.hide();
    } else {
      exit(0);
    }
  }

  void dispose() {
    windowManager.removeListener(this);
  }
}

class UnixSocketHttpOverrides extends HttpOverrides {
  final String socketPath;
  UnixSocketHttpOverrides(this.socketPath);

  @override
  HttpClient createHttpClient(SecurityContext? context) {
    final client = super.createHttpClient(context);
    client.connectionFactory = (url, proxyHost, proxyPort) {
      if (url.host == 'unix') {
        return Socket.startConnect(
          InternetAddress(socketPath, type: InternetAddressType.unix),
          0,
        );
      }
      return Socket.startConnect(url.host, url.port);
    };
    return client;
  }
}
