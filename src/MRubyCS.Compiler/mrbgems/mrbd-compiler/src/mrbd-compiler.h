#ifndef MRUBYD_COMPILER_H
#define MRUBYD_COMPILER_H

#include <mruby.h>
#include <mruby/proc.h>

extern int32_t mrbd_compile(mrb_state *mrb,
                            const uint8_t *source,
                            uint32_t source_length,
                            uint8_t **bin,
                            int32_t *bin_size,
                            char **error_message);

#endif
