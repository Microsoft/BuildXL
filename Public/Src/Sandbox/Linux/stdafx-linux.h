// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif

#include <assert.h>
#include <fcntl.h>
#include <limits.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <wchar.h>
#include <sys/stat.h>

#include <atomic>
#include <cstring>
#include <iostream>
#include <istream>
#include <memory>
#include <string>

#define MAXPATHLEN PATH_MAX

#define BUILDXL_BUNDLE_IDENTIFIER "com.microsoft.buildxl.sandbox"
#define BUILDXL_CLASS_PREFIX "com_microsoft_buildxl_"

#define __cdecl  // __attribute__((__cdecl__))
#define strlcpy strncpy
#define os_log_error 
#define os_log

typedef uint64_t mach_vm_address_t;
typedef uint64_t mach_vm_offset_t;
typedef uint64_t mach_vm_size_t;

typedef uint64_t vm_map_offset_t;
typedef uint64_t vm_map_address_t;
typedef uint64_t vm_map_size_t;

// bogus type definitions
typedef void* os_log_t;
typedef void* es_client_t;
typedef void* dispatch_queue_t;

// ES events
typedef struct{
	unsigned int val[8];
} audit_token_t;

#define es_mute_process(...)

typedef enum {
    // The following events are available beginning in macOS 10.15
    ES_EVENT_TYPE_AUTH_EXEC
  , ES_EVENT_TYPE_AUTH_OPEN
  , ES_EVENT_TYPE_AUTH_KEXTLOAD
  , ES_EVENT_TYPE_AUTH_MMAP
  , ES_EVENT_TYPE_AUTH_MPROTECT
  , ES_EVENT_TYPE_AUTH_MOUNT
  , ES_EVENT_TYPE_AUTH_RENAME
  , ES_EVENT_TYPE_AUTH_SIGNAL
  , ES_EVENT_TYPE_AUTH_UNLINK
  , ES_EVENT_TYPE_NOTIFY_EXEC
  , ES_EVENT_TYPE_NOTIFY_OPEN
  , ES_EVENT_TYPE_NOTIFY_FORK
  , ES_EVENT_TYPE_NOTIFY_CLOSE
  , ES_EVENT_TYPE_NOTIFY_CREATE
  , ES_EVENT_TYPE_NOTIFY_EXCHANGEDATA
  , ES_EVENT_TYPE_NOTIFY_EXIT
  , ES_EVENT_TYPE_NOTIFY_GET_TASK
  , ES_EVENT_TYPE_NOTIFY_KEXTLOAD
  , ES_EVENT_TYPE_NOTIFY_KEXTUNLOAD
  , ES_EVENT_TYPE_NOTIFY_LINK
  , ES_EVENT_TYPE_NOTIFY_MMAP
  , ES_EVENT_TYPE_NOTIFY_MPROTECT
  , ES_EVENT_TYPE_NOTIFY_MOUNT
  , ES_EVENT_TYPE_NOTIFY_UNMOUNT
  , ES_EVENT_TYPE_NOTIFY_IOKIT_OPEN
  , ES_EVENT_TYPE_NOTIFY_RENAME
  , ES_EVENT_TYPE_NOTIFY_SETATTRLIST
  , ES_EVENT_TYPE_NOTIFY_SETEXTATTR
  , ES_EVENT_TYPE_NOTIFY_SETFLAGS
  , ES_EVENT_TYPE_NOTIFY_SETMODE
  , ES_EVENT_TYPE_NOTIFY_SETOWNER
  , ES_EVENT_TYPE_NOTIFY_SIGNAL
  , ES_EVENT_TYPE_NOTIFY_UNLINK
  , ES_EVENT_TYPE_NOTIFY_WRITE
  , ES_EVENT_TYPE_AUTH_FILE_PROVIDER_MATERIALIZE
  , ES_EVENT_TYPE_NOTIFY_FILE_PROVIDER_MATERIALIZE
  , ES_EVENT_TYPE_AUTH_FILE_PROVIDER_UPDATE
  , ES_EVENT_TYPE_NOTIFY_FILE_PROVIDER_UPDATE
  , ES_EVENT_TYPE_AUTH_READLINK
  , ES_EVENT_TYPE_NOTIFY_READLINK
  , ES_EVENT_TYPE_AUTH_TRUNCATE
  , ES_EVENT_TYPE_NOTIFY_TRUNCATE
  , ES_EVENT_TYPE_AUTH_LINK
  , ES_EVENT_TYPE_NOTIFY_LOOKUP
  , ES_EVENT_TYPE_AUTH_CREATE
  , ES_EVENT_TYPE_AUTH_SETATTRLIST
  , ES_EVENT_TYPE_AUTH_SETEXTATTR
  , ES_EVENT_TYPE_AUTH_SETFLAGS
  , ES_EVENT_TYPE_AUTH_SETMODE
  , ES_EVENT_TYPE_AUTH_SETOWNER
    // The following events are available beginning in macOS 10.15.1
  , ES_EVENT_TYPE_AUTH_CHDIR
  , ES_EVENT_TYPE_NOTIFY_CHDIR
  , ES_EVENT_TYPE_AUTH_GETATTRLIST
  , ES_EVENT_TYPE_NOTIFY_GETATTRLIST
  , ES_EVENT_TYPE_NOTIFY_STAT
  , ES_EVENT_TYPE_NOTIFY_ACCESS
  , ES_EVENT_TYPE_AUTH_CHROOT
  , ES_EVENT_TYPE_NOTIFY_CHROOT
  , ES_EVENT_TYPE_AUTH_UTIMES
  , ES_EVENT_TYPE_NOTIFY_UTIMES
  , ES_EVENT_TYPE_AUTH_CLONE
  , ES_EVENT_TYPE_NOTIFY_CLONE
  , ES_EVENT_TYPE_NOTIFY_FCNTL
  , ES_EVENT_TYPE_AUTH_GETEXTATTR
  , ES_EVENT_TYPE_NOTIFY_GETEXTATTR
  , ES_EVENT_TYPE_AUTH_LISTEXTATTR
  , ES_EVENT_TYPE_NOTIFY_LISTEXTATTR
  , ES_EVENT_TYPE_AUTH_READDIR
  , ES_EVENT_TYPE_NOTIFY_READDIR
  , ES_EVENT_TYPE_AUTH_DELETEEXTATTR
  , ES_EVENT_TYPE_NOTIFY_DELETEEXTATTR
  , ES_EVENT_TYPE_AUTH_FSGETPATH
  , ES_EVENT_TYPE_NOTIFY_FSGETPATH
  , ES_EVENT_TYPE_NOTIFY_DUP
  , ES_EVENT_TYPE_AUTH_SETTIME
  , ES_EVENT_TYPE_NOTIFY_SETTIME
  , ES_EVENT_TYPE_NOTIFY_UIPC_BIND
  , ES_EVENT_TYPE_AUTH_UIPC_BIND
  , ES_EVENT_TYPE_NOTIFY_UIPC_CONNECT
  , ES_EVENT_TYPE_AUTH_UIPC_CONNECT
  , ES_EVENT_TYPE_AUTH_EXCHANGEDATA
  , ES_EVENT_TYPE_AUTH_SETACL
  , ES_EVENT_TYPE_NOTIFY_SETACL
	// The following events are available beginning in macOS 10.15.4
  , ES_EVENT_TYPE_NOTIFY_PTY_GRANT
  , ES_EVENT_TYPE_NOTIFY_PTY_CLOSE
  , ES_EVENT_TYPE_AUTH_PROC_CHECK
  , ES_EVENT_TYPE_NOTIFY_PROC_CHECK
  , ES_EVENT_TYPE_AUTH_GET_TASK
    // ES_EVENT_TYPE_LAST is not a valid event type but a convenience
    // value for operating on the range of defined event types.
    // This value may change between releases and was available
    // beginning in macOS 10.15
  , ES_EVENT_TYPE_LAST
} es_event_type_t;
