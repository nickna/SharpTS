# Array spread indexed descriptors — #1742

The four `original-*` programs preserve the issue's exact sources. Fresh
unchanged main skips the indexed getter in array/argument spread, while direct
indexing and Array.values already match Node. All baseline saved outputs verify
their IL, exit within the original 30-second deadline and have empty stderr.

Eleven references run in compiled execution and isolated standalone saved
output. They check dense/hole getters, order, growth/shrinkage of live length,
getter exception identity, suppressed call invocation after argument failure,
iterator overrides, repeated spreading, sparse-hole materialization and the
numeric argument-spread control. Node executes and TypeScript checks each
source separately. Saved output requires exact stdout, empty stderr, clean exit
within 30 seconds, and no SharpTS assembly reference/copy.

The collection helpers use live ordinary indexed reads when a descriptor is
present, preserving custom-iterator selection before that path. Native tests
also cover both List-backed and emitted-array representations across reused
minimal, optional, mutation and hosted emission. Descriptor-free dense storage
retains its bulk argument-spread path, and dense collection converts internal
holes to actual undefined entries.

No interpreter or hosted guest execution repair, unrelated array-method
descriptor expansion or arbitrary iterable protocol change is claimed.
