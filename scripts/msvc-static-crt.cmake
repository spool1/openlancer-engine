# OpenLancer (OL-0035): links the MSVC builds of the natives with the static C and C++ runtime (/MT).
# A PC without the Microsoft Visual C++ Redistributable can then load the natives.
# BuildLL gives this file to CMake as CMAKE_TOOLCHAIN_FILE. blurgtext gives it to its FreeType and HarfBuzz builds.
# Each native frees its own memory, so a separate runtime in each DLL is safe.

# Projects with policy CMP0091 NEW (cmake_minimum_required 3.15 or later) use this variable.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

# Projects with policy CMP0091 OLD (HarfBuzz 9.0.0 and thorncompiler) get the runtime from the default flags.
# A toolchain file cannot set that policy, because CMake applies the policy defaults before it reads this file.
# So the second file changes the default flags.
set(CMAKE_USER_MAKE_RULES_OVERRIDE "${CMAKE_CURRENT_LIST_DIR}/msvc-static-crt-flags.cmake")
