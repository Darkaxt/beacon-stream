set(required_files
  "${GENERATED_DIR}/worker_ipc.pb.h"
  "${GENERATED_DIR}/worker_ipc.pb.cc"
  "${GENERATED_DIR}/stream_control.pb.h"
  "${GENERATED_DIR}/stream_control.pb.cc"
)

foreach(required_file IN LISTS required_files)
  if(NOT EXISTS "${required_file}")
    message(FATAL_ERROR "Generated contract is missing: ${required_file}")
  endif()
endforeach()

file(READ "${GENERATED_DIR}/worker_ipc.pb.h" worker_header)
file(READ "${GENERATED_DIR}/stream_control.pb.h" stream_header)
if(NOT worker_header MATCHES "class WorkerIpcEnvelope")
  message(FATAL_ERROR "Worker IPC C++ contract does not contain WorkerIpcEnvelope.")
endif()
if(NOT stream_header MATCHES "class PublicSessionEnvelope")
  message(FATAL_ERROR "Stream C++ contract does not contain PublicSessionEnvelope.")
endif()
