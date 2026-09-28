/* Compile-time checks of the public ABI used by the managed 64-bit binding.
 * Cross-compilation checks these on BOTH Android ABIs without executing them. */
#include <stddef.h>
#include "dav1d/dav1d.h"

#define SIZE(type, size) _Static_assert(sizeof(type) == size, #type " size")
#define OFFSET(type, field, offset) \
    _Static_assert(offsetof(type, field) == offset, #type "." #field " offset")

SIZE(Dav1dSettings, 96);
SIZE(Dav1dPicAllocator, 24);
SIZE(Dav1dLogger, 16);
SIZE(Dav1dPicture, 272);
SIZE(Dav1dPictureParameters, 16);
SIZE(Dav1dData, 72);
SIZE(Dav1dDataProps, 48);
SIZE(Dav1dUserData, 16);
SIZE(Dav1dSequenceHeader, 808);
SIZE(Dav1dFrameHeader, 1152);
SIZE(Dav1dContentLightLevel, 4);
SIZE(Dav1dMasteringDisplay, 24);
OFFSET(Dav1dSettings, allocator, 24);
OFFSET(Dav1dSettings, logger, 48);
OFFSET(Dav1dSettings, reserved, 80);
OFFSET(Dav1dPicture, data, 16);
OFFSET(Dav1dPicture, stride, 40);
OFFSET(Dav1dPicture, p, 56);
OFFSET(Dav1dPicture, m, 72);
OFFSET(Dav1dPicture, ref, 256);
OFFSET(Dav1dPicture, allocator_data, 264);
OFFSET(Dav1dData, m, 24);
OFFSET(Dav1dSequenceHeader, max_width, 4);
OFFSET(Dav1dSequenceHeader, layout, 12);
OFFSET(Dav1dFrameHeader, frame_type, 232);
OFFSET(Dav1dFrameHeader, width, 236);
OFFSET(Dav1dFrameHeader, height, 244);
OFFSET(Dav1dFrameHeader, show_frame, 264);
OFFSET(Dav1dFrameHeader, render_width, 408);
OFFSET(Dav1dFrameHeader, render_height, 412);
