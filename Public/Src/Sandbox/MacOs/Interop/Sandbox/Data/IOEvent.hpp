// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef IOEvent_hpp
#define IOEvent_hpp

#include "stdafx.h"

#include <iostream>
#include <istream>

#include "MemoryStreams.hpp"

#define SRC_PATH 0
#define DST_PATH 1

#if __APPLE__
#include "PathExtractor.hpp"
#endif

// See: https://opensource.apple.com/source/xnu/xnu-1699.24.23/bsd/sys/proc_internal.h
#define PID_MAX 99999

#define DETOURS_BUNDLE_IDENTIFIER "com.microsoft.buildxl.detours"

#define ES_EVENT_CONSTRUCTOR(type, dir, file, mode, do_break) \
    es_event_##type##_t event = msg->event.type; \
    src_path_ = PathExtractor(event.file).Path(); \
    if (mode) {mode_ = event.file->stat.st_mode; } \
    if (do_break) break;

struct IOEvent final
{
    friend omemorystream& operator<<(omemorystream &os, const IOEvent &event);
    friend imemorystream& operator>>(imemorystream &is, IOEvent &event);

private:

    pid_t pid_;
    pid_t cpid_;
    pid_t ppid_;
    es_event_type_t eventType_;
    mode_t mode_ = 0;
    bool modified_ = false;

    std::string executable_;
    std::string src_path_;
    std::string dst_path_;

    // Only used when the IOEvent is backed by an EndpointSecurity message
    pid_t oppid_;
    audit_token_t auditToken_;
    
public:

    IOEvent() {}

#if __APPLE__
    IOEvent(const es_message_t *msg);
#endif

    IOEvent(pid_t pid,
            pid_t cpid,
            pid_t ppid,
            es_event_type_t type,
            const char *src, const char *dst,
            const std::string exec,
            bool get_mode = true,
            bool modified = false)
    : pid_(pid), cpid_(cpid), ppid_(ppid), eventType_(type), modified_(modified)
    {
        assert(!exec.empty());
        executable_ = exec;

        src_path_ = src != nullptr ? std::string(src) : std::string("");
        dst_path_ = dst != nullptr ? std::string(dst) : std::string("");

        oppid_ = ppid_;

        if (get_mode)
        {
            struct stat s;
            mode_ = stat(src_path_.c_str(), &s) == 0 ? s.st_mode : 0;
        }
    }

    IOEvent(pid_t pid,
            pid_t cpid,
            pid_t ppid,
            es_event_type_t type,
            std::string src, 
            std::string dst,
            const std::string exec,
            mode_t mode,
            bool modified = false)
    : pid_(pid), cpid_(cpid), ppid_(ppid), oppid_(ppid), eventType_(type), src_path_(src), dst_path_(dst), executable_(exec), mode_(mode), modified_(modified)
    {
    }

    IOEvent(es_event_type_t type,
            std::string src, 
            const std::string exec,
            mode_t mode,
            bool modified = false,
            std::string dest = "")
    : IOEvent(getpid(), 0, getppid(), type, src, dest, exec, mode, modified)
    {
    }

    inline const pid_t GetPid() const { return pid_; }
    inline const pid_t GetParentPid() const { return ppid_; }
    inline const pid_t GetChildPid() const { return cpid_; }
    inline const pid_t GetOriginalParentPid() const { return oppid_; }
    inline const char* GetExecutablePath() const { return executable_.c_str(); }

    inline const audit_token_t* GetProcessAuditToken() const { return &auditToken_; }
    inline const es_event_type_t GetEventType() const { return eventType_; }

    inline const std::string& GetSrcPath() const { return src_path_; }
    inline const std::string& GetDstPath() const { return dst_path_; }

    inline const char* GetEventPath(int index = SRC_PATH) const { return (index == SRC_PATH ? src_path_ : dst_path_).c_str(); }
    inline void SetEventPath(char *value, int index = SRC_PATH)
    {
        if (index == SRC_PATH)
        {
            src_path_ = std::string(value);
        }
        else
        {
            dst_path_ = std::string(value);
        }
    }
    
    inline const mode_t GetMode() const { return mode_; }
    inline const bool FSEntryModified() const { return modified_; }
    inline const bool EventPathExists() const { return mode_ != 0; }
    
    const bool IsPlistEvent() const;
    const bool IsDirectorySpecialCharacterEvent() const;

    const size_t Size() const;

    // Keep this in sync with the de- and serialization logic in the implementation file (IOEvent.cpp), especially Size()!
    static inline const size_t max_size()
    {
        return (3 * std::to_string(PID_MAX).length()) + // Pids
               (2 * std::to_string(USHRT_MAX).length()) + // Type + Mode
               std::to_string(true).length() + // Modified
               (3 * PATH_MAX) + // Executable, src and dst paths
               9; // Delimiters
    }
};

enum IOEventBacking
{
    EndpointSecurity = 0,
    Interposing
};

enum ProcessCallbackResult
{
    Done = 0,
    MuteSource
};

typedef ProcessCallbackResult (*process_callback)(void *sandbox, const IOEvent event, pid_t host, IOEventBacking backing);

#endif /* IOEvent_hpp */
