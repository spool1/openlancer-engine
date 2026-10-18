# OpenLancer (OL-0035): CMake reads this file after the platform files set the default flags, and before it puts
# the default flags into the cache. See msvc-static-crt.cmake.
# Each default flag set gets the static runtime flag (/MT, or /MTd for Debug) in place of a runtime flag.
# For a project with policy CMP0091 NEW, this flag is the same as the flag of CMAKE_MSVC_RUNTIME_LIBRARY.
if(MSVC)
  foreach(lang C CXX)
    foreach(config DEBUG RELEASE MINSIZEREL RELWITHDEBINFO)
      set(flags " ${CMAKE_${lang}_FLAGS_${config}_INIT} ")
      string(REGEX REPLACE " [/-]M[DT]d?( [/-]M[DT]d?)* " " " flags "${flags}")
      if(config STREQUAL "DEBUG")
        string(APPEND flags "/MTd")
      else()
        string(APPEND flags "/MT")
      endif()
      string(STRIP "${flags}" flags)
      set(CMAKE_${lang}_FLAGS_${config}_INIT "${flags}")
    endforeach()
  endforeach()
endif()
