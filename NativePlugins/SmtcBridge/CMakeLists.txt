cmake_minimum_required(VERSION 3.15)
project(ChillPatcher_SmtcBridge VERSION 1.0.0 LANGUAGES CXX)

# C++/WinRT 需要 C++17 或更高
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# 设置输出目录
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)

# 头文件路径
include_directories(${CMAKE_SOURCE_DIR}/include)

# ========== SMTC Bridge 库 ==========
set(LIBRARY_SOURCES
    src/smtc_bridge.cpp
)

add_library(ChillSmtcBridge SHARED ${LIBRARY_SOURCES})

# Windows 特定设置
if(WIN32)
    target_compile_definitions(ChillSmtcBridge PRIVATE
        UNICODE
        _UNICODE
        WIN32_LEAN_AND_MEAN
        WINRT_LEAN_AND_MEAN
    )
    
    # 导出所有符号
    set_target_properties(ChillSmtcBridge PROPERTIES
        PREFIX ""
        SUFFIX ".dll"
    )
    
    # MSVC 设置
    if(MSVC)
        # 启用协程支持
        target_compile_options(ChillSmtcBridge PRIVATE /await:strict)
        
        # 使用静态运行时库（避免依赖 MSVC 运行时 DLL）
        set_property(TARGET ChillSmtcBridge PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
        
        # 禁用特定警告
        target_compile_options(ChillSmtcBridge PRIVATE /wd4100 /wd4189)
    endif()
    
    # 链接必要的库
    target_link_libraries(ChillSmtcBridge PRIVATE
        windowsapp.lib
        RuntimeObject.lib
    )
endif()

# ========== 控制台测试程序 ==========
set(TEST_SOURCES
    test/smtc_test.cpp
)

add_executable(SmtcTest ${TEST_SOURCES})

if(WIN32)
    target_compile_definitions(SmtcTest PRIVATE
        UNICODE
        _UNICODE
        WIN32_LEAN_AND_MEAN
        WINRT_LEAN_AND_MEAN
    )
    
    if(MSVC)
        target_compile_options(SmtcTest PRIVATE /await:strict)
        set_property(TARGET SmtcTest PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    endif()
    
    target_link_libraries(SmtcTest PRIVATE
        ChillSmtcBridge
        windowsapp.lib
        RuntimeObject.lib
    )
endif()

# 设置 test 程序的工作目录
set_target_properties(SmtcTest PROPERTIES
    VS_DEBUGGER_WORKING_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)

# ========== 安装规则 ==========
set(NATIVE_OUTPUT_DIR "${CMAKE_SOURCE_DIR}/../../bin/native")
install(TARGETS ChillSmtcBridge
    RUNTIME DESTINATION "${NATIVE_OUTPUT_DIR}/$<IF:$<EQUAL:${CMAKE_SIZEOF_VOID_P},8>,x64,x86>"
    LIBRARY DESTINATION "${NATIVE_OUTPUT_DIR}/$<IF:$<EQUAL:${CMAKE_SIZEOF_VOID_P},8>,x64,x86>"
)

# 打印配置信息
message(STATUS "========================================")
message(STATUS "ChillPatcher SMTC Bridge Native Plugin")
message(STATUS "========================================")
message(STATUS "Build Type: ${CMAKE_BUILD_TYPE}")
message(STATUS "Platform: ${CMAKE_SYSTEM_NAME}")
message(STATUS "Architecture: ${CMAKE_SYSTEM_PROCESSOR}")
message(STATUS "C++ Standard: ${CMAKE_CXX_STANDARD}")
message(STATUS "Output: ${CMAKE_RUNTIME_OUTPUT_DIRECTORY}")
message(STATUS "========================================")
