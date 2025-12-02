// SMTC Bridge Test Program
// 用于测试 SMTC 功能的控制台程序

#include <iostream>
#include <thread>
#include <chrono>
#include <string>
#include "../include/smtc_bridge.h"

// 按钮回调
void OnButtonPressed(SmtcButtonType button)
{
    const char* buttonName = "Unknown";
    switch (button)
    {
    case SMTC_BUTTON_PLAY:
        buttonName = "Play";
        break;
    case SMTC_BUTTON_PAUSE:
        buttonName = "Pause";
        break;
    case SMTC_BUTTON_STOP:
        buttonName = "Stop";
        break;
    case SMTC_BUTTON_PREVIOUS:
        buttonName = "Previous";
        break;
    case SMTC_BUTTON_NEXT:
        buttonName = "Next";
        break;
    default:
        buttonName = "Other";
        break;
    }
    std::cout << "[Callback] Button pressed: " << buttonName << std::endl;
}

int main()
{
    std::cout << "=== SMTC Bridge Test ===" << std::endl;
    
    // 初始化
    std::cout << "Initializing SMTC..." << std::endl;
    int result = SmtcInitialize();
    if (result != 0)
    {
        std::cerr << "Failed to initialize SMTC! Error code: " << result << std::endl;
        return 1;
    }
    std::cout << "SMTC initialized successfully!" << std::endl;
    
    // 设置按钮回调
    SmtcSetButtonPressedCallback(OnButtonPressed);
    
    // 设置音乐信息
    std::cout << "Setting music info..." << std::endl;
    SmtcSetMusicInfo(L"Test Song Title", L"Test Artist", nullptr);
    
    // 设置播放状态
    std::cout << "Setting playback status to Playing..." << std::endl;
    SmtcSetPlaybackStatus(SMTC_PLAYBACK_PLAYING);
    
    // 等待用户交互
    std::cout << std::endl;
    std::cout << "SMTC is now active. You should see media info in Windows media overlay." << std::endl;
    std::cout << "Press Win+G or volume keys to see the media controls." << std::endl;
    std::cout << std::endl;
    std::cout << "Commands:" << std::endl;
    std::cout << "  p - Set to Playing" << std::endl;
    std::cout << "  s - Set to Paused" << std::endl;
    std::cout << "  t - Set to Stopped" << std::endl;
    std::cout << "  q - Quit" << std::endl;
    std::cout << std::endl;
    
    char cmd;
    while (true)
    {
        std::cout << "> ";
        std::cin >> cmd;
        
        if (cmd == 'q' || cmd == 'Q')
            break;
        
        switch (cmd)
        {
        case 'p':
        case 'P':
            std::cout << "Setting playback status to Playing..." << std::endl;
            SmtcSetPlaybackStatus(SMTC_PLAYBACK_PLAYING);
            break;
        case 's':
        case 'S':
            std::cout << "Setting playback status to Paused..." << std::endl;
            SmtcSetPlaybackStatus(SMTC_PLAYBACK_PAUSED);
            break;
        case 't':
        case 'T':
            std::cout << "Setting playback status to Stopped..." << std::endl;
            SmtcSetPlaybackStatus(SMTC_PLAYBACK_STOPPED);
            break;
        default:
            std::cout << "Unknown command: " << cmd << std::endl;
            break;
        }
    }
    
    // 清理
    std::cout << "Shutting down SMTC..." << std::endl;
    SmtcShutdown();
    std::cout << "Done!" << std::endl;
    
    return 0;
}
