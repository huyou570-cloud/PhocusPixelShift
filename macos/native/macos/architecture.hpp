// Original adapter offsets, resolved independently from same-name local symbols
// in the two slices of the dependency-verified universal Phocus frameworks.
#pragma once
inline constexpr size_t nativeOffset(size_t arm, size_t intel) {
#if defined(__aarch64__)
    return arm;
#elif defined(__x86_64__)
    return intel;
#else
#error Unsupported CPU architecture
#endif
}
