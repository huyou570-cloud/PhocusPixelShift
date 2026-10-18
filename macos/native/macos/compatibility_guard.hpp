// Original adapter guard. This hashes user-installed files; no vendor bytes embedded.
#pragma once
#include <CommonCrypto/CommonDigest.h>
#include <fstream>
#include <sstream>
#include <iomanip>
inline void verifyInstalledPhocus() {
#if !defined(__aarch64__) && !defined(__x86_64__)
    throw std::runtime_error("This adapter requires ARM64 or x86_64");
#endif
    const std::pair<const char*, const char*> expected[] = {
        {"Contents/Frameworks/HBCamera.framework/Versions/A/HBCamera", "6b326ba293ddfa90dfad05c5fd6f06114183ba3345239a19842acfffea7971e8"},
        {"Contents/Frameworks/HBWorkFlow.framework/Versions/A/HBWorkFlow", "8066bd872844394466607ba81d1020bb979c50f43f9c9ab4fd388e34b013de18"},
        {"Contents/Frameworks/HBImageProcessing.framework/Versions/A/HBImageProcessing", "e7373e81598dddcdf71e5b12c463c1c89039deccaae073d76590c36392b20db4"},
        {"Contents/Frameworks/HBStorage.framework/Versions/A/HBStorage", "f6deef8868a5dbbdbd5538e569877e06197173a9d83fce691fb98e3707750037"},
        {"Contents/Frameworks/HBRawCorrections.framework/Versions/A/HBRawCorrections", "cc1385cc6034c22a816798163c1fee4d0f1152947651db44605a876da51cc20d"},
        {"Contents/Frameworks/HBUtil.framework/Versions/A/HBUtil", "9763026e22bb0a08ef00902faa20e414161f95d71fbd6aefc897e0278dcc5c6c"},
        {"Contents/Resources/English.lproj/Localizable.strings", "f01d1e514de68bdfcf29ffee446081b7ab2b521ae58bed1b93c08b1765334b12"},
    };
    for (const auto &entry : expected) {
        std::string path = std::string("/Applications/Phocus.app/") + entry.first;
        std::ifstream file(path, std::ios::binary);
        if(!file) throw std::runtime_error("Missing Phocus component: " + path);
        CC_SHA256_CTX ctx; CC_SHA256_Init(&ctx);
        std::array<char, 1024*1024> buffer;
        while(file) {file.read(buffer.data(), buffer.size()); auto n=file.gcount();
            if(n>0) CC_SHA256_Update(&ctx, buffer.data(), (CC_LONG)n);}
        if(!file.eof()) throw std::runtime_error("Cannot hash Phocus component: " + path);
        unsigned char bytes[CC_SHA256_DIGEST_LENGTH]; CC_SHA256_Final(bytes, &ctx);
        std::ostringstream hex;
        for(auto value:bytes) hex << std::hex << std::setw(2) << std::setfill('0') << (unsigned)value;
        if(hex.str()!=entry.second) throw std::runtime_error("Unsupported Phocus component hash: " + path);
    }
}
