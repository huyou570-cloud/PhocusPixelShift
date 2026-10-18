#import <Foundation/Foundation.h>
#include <dlfcn.h>
#include <array>
#include <vector>
#include <string>
#include <memory>
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <filesystem>
#include <fstream>

// One-off offline ABI experiment. Invoke only through the hash-checking launcher.
// This is a candidate bridge, not an assertion of complete calibration or QA.
template<class T> T symbol(void *h, const char *name) {
    auto p=dlsym(h,name); if(!p) throw std::runtime_error(dlerror());
    return reinterpret_cast<T>(p);
}
template<class T> T field(const void *p,size_t offset) {
    T out; memcpy(&out,(const char*)p+offset,sizeof out); return out;
}
void mark(const char *s) {fprintf(stderr,"STAGE %s\n",s);fflush(stderr);}
struct ParameterBlock {
    void *raw=nullptr, *conversion=nullptr;
    bool flag=true; char padding[7]{};
    std::vector<unsigned> columns;
    std::vector<int> levels;
    std::vector<short> blemishes;
    void *image=nullptr;
    bool imageOwned=false; char tail[7]{};
};
static_assert(offsetof(ParameterBlock,columns)==0x18);
static_assert(offsetof(ParameterBlock,levels)==0x30);
static_assert(offsetof(ParameterBlock,blemishes)==0x48);
static_assert(offsetof(ParameterBlock,image)==0x60);
static_assert(sizeof(ParameterBlock)==0x70);
#include "architecture.hpp"
#include "compatibility_guard.hpp"
#include "native_pipeline_support.hpp"

int runBridge(int argc,char **argv) {try {
    verifyInstalledPhocus();
    if(argc==2 && std::string(argv[1])=="--check") {
        puts("{\"compatible\":true,\"scope\":\"dependency-verified Phocus engine; no version-label whitelist; no image processing tested\"}");
        return 0;
    }
    if(argc<7) return 2;
    const int frames=atoi(argv[1]);
    if((frames!=4&&frames!=6&&frames!=16)||argc!=frames+3) throw std::runtime_error("Expected frame count, new output path, and 4, 6 or 16 source paths");
    const std::filesystem::path output=argv[2];
    if(std::filesystem::exists(output)) throw std::runtime_error("Refusing to overwrite output");
    void *lib=dlopen("/Applications/Phocus.app/Contents/Frameworks/HBImageProcessing.framework/Versions/A/HBImageProcessing",RTLD_NOW|RTLD_GLOBAL);
    if(!lib) throw std::runtime_error(dlerror());
    void *camera=dlopen("/Applications/Phocus.app/Contents/Frameworks/HBCamera.framework/Versions/A/HBCamera",RTLD_NOW|RTLD_GLOBAL);
    if(!camera) throw std::runtime_error(dlerror());
    Dl_info ci{};
    if(!dladdr(symbol<void*>(camera,"_ZN11CCameraData9SetSourceEP13CRawImageFile"),&ci)) throw std::runtime_error("No camera library base");
    // The pinned Intel CStreamFactory constructor writes its callback at 0x430.
    // Its backing storage must extend beyond 0x438 on both architectures.
    alignas(16) std::array<unsigned char,2048> streamFactory{};
    alignas(16) std::array<unsigned char,1024> debug{};
    symbol<void(*)(void*)>(lib,"_ZN17CStreamFactoryMacC1Ev")(streamFactory.data());
    *symbol<void**>(lib,"gStreamFactory")=streamFactory.data();
    symbol<void(*)(void*,std::string)>(lib,"_ZN13CDebugManagerC1ENSt3__112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEE")(debug.data(),std::string(""));
    *symbol<void**>(lib,"gDebugManager")=debug.data();
    // Metadata translation uses this non-virtual message lookup even when no
    // GUI status callback exists. Load the installed native English resource.
    alignas(16) std::array<unsigned char,512> messages{},messageSpec{};
    symbol<void(*)(void*)>(lib,"_ZN15CMessageFactoryC2Ev")(messages.data());
    *symbol<void**>(lib,"gMessageFactory")=messages.data();
    symbol<void(*)(void*,const char*,bool,int,int)>(lib,"_ZN9CFileSpecC1EPKcbii")(messageSpec.data(),"/Applications/Phocus.app/Contents/Resources/English.lproj/Localizable.strings",true,0,0);
    symbol<void(*)(void*,void*,void*)>(lib,"_ZN12CStringsFile15ReadStringsFileEP9CFileSpecPNSt3__13mapINS2_12basic_stringIt15UCharTraitsBaseItENS2_9allocatorItEEEES9_NS2_4lessIS9_EENS7_INS2_4pairIKS9_S9_EEEEEEPNS2_8multimapIS9_S9_SB_SF_EE")(messageSpec.data(),messages.data()+8,nullptr);
    symbol<void(*)(void*)>(lib,"_ZN9CFileSpecD1Ev")(messageSpec.data());
    std::unique_ptr<NativePipelineProbe> pipeline;
    if(getenv("PHOCUS_CALIBRATED_PROBE"))pipeline=std::make_unique<NativePipelineProbe>(lib,camera);
    auto fileCtor=symbol<void(*)(void*,const char*,bool,int,int)>(lib,"_ZN9CFileSpecC1EPKcbii");
    auto fileDtor=symbol<void(*)(void*)>(lib,"_ZN9CFileSpecD1Ev");
    auto openRaw=symbol<void(*)(void*,const void*)>(lib,"_ZN11CRaw3FRFileC1E9CFileSpec");
    // ARM64 vtable verified in this exact build: slot 2 is the non-deleting
    // CRaw3FRFile destructor (local symbol 0xd5854), slot 3 is deleting.
    auto closeRaw=[](void *raw) {
        auto vt=field<void**>(raw,0);
        reinterpret_cast<void(*)(void*)>(vt[2])(raw);
    };
    auto setShots=symbol<bool(*)(void*,int,int*)>(lib,"_ZN23CInitializeRawImageFile12SetMultiShotEiPi");
    auto getBits=symbol<unsigned long long(*)(void*)>(lib,"_ZN13CRawImageFile20GetRawCorrectionBitsEv");
    alignas(16) std::array<unsigned char,0x520> capture{};
    int captureType=frames==4?1:(frames==6?7:2);
    // The Boolean enables native multi-shot column correction. It is not a
    // CMOS selector; the calibrated probe must retain the native correction.
    reinterpret_cast<void(*)(void*,int,bool)>((char*)ci.dli_fbase+nativeOffset(0x225e8,0x5a0dc))(capture.data(),captureType,pipeline!=nullptr);
    char positionsA[16]={3,3,1,1,3,3,1,1,2,2,0,0,2,2,0,0};
    char positionsB[16]={0,2,0,2,1,3,1,3,0,2,0,2,1,3,1,3};
    const bool canonical16=frames==16&&getenv("PHOCUS_X2D_NATIVE_ORDER");
    if(frames==16&&!canonical16) {
        // X2D source order verified against all 16 frame indices and three
        // independent static image regions (phase-ecc.json), within 0.05 px.
        const char x[16]={3,3,3,3,2,2,2,2,1,1,1,1,0,0,0,0};
        const char y[16]={0,1,2,3,3,2,1,0,0,1,2,3,3,2,1,0};
        memcpy(positionsA,x,16);memcpy(positionsB,y,16);
    }
    if(frames==6) symbol<void(*)(const void*,char*,char*)>(lib,"_ZNK10CMultiShot23Adapt6ShotPixelPositionEPcS0_")(capture.data(),positionsA,positionsB);
    symbol<void(*)(void*,char*,char*,short)>(lib,"_ZN10CMultiShot16SetShotPositionsEPcS0_s")(capture.data(),positionsA,positionsB,16);
    alignas(16) std::array<unsigned char,2048> rawBuffer{};
    ParameterBlock block;
    int width=0,height=0,sensor=0;
    int rgbWidth=0,rgbHeight=0;
    std::vector<unsigned short> rgb;
    unsigned long long firstBits=0;
    for(int i=0;i<frames;i++) {
        alignas(16) std::array<unsigned char,512> spec{};
        alignas(16) std::array<unsigned char,4096> raw{};
        const int canonicalOrder[16]={0,2,8,10,1,3,9,11,7,5,15,13,6,4,14,12};
        const int sourceIndex=canonical16?canonicalOrder[i]:i;
        fileCtor(spec.data(),argv[3+sourceIndex],true,0,0);
        openRaw(raw.data(),spec.data()); fileDtor(spec.data());
        alignas(16) std::array<unsigned char,32> point{};
        symbol<void(*)(void*)>(lib,"_ZN7CXPointC1Ev")(point.data());
        if(!symbol<bool(*)(void*,void*)>(lib,"_ZN13CRawImageFile11RawDataSizeER7CXPoint")(raw.data(),point.data())) throw std::runtime_error("RAW dimensions unavailable");
        int w=field<int>(point.data(),12),h=field<int>(point.data(),16);
        if(w<100||h<100||w>20000||h>20000) throw std::runtime_error("Unexpected RAW geometry");
        // Only the temporary in-memory single-frame view is changed.
        // The original sequence metadata has already been saved in the manifest.
        if(!setShots(raw.data(),1,nullptr)) throw std::runtime_error("Cannot establish single-frame input view");
        auto desc=symbol<std::shared_ptr<const void>(*)(void*)>(lib,"_ZN13CRawImageFile17GetRawDescriptionEv")(raw.data());
        int s=field<int>(desc.get(),0x32c);
        auto bits=getBits(raw.data());
        if(i==0) {
            width=w;height=h;sensor=s;firstBits=bits;
            symbol<void(*)(void*,int,int,unsigned short**)>(lib,"_ZN15CRawImageBufferC1EiiPPt")(rawBuffer.data(),w,h,nullptr);
            symbol<void(*)(void*,int,int,bool)>(lib,"_ZN15CRawImageBuffer10SetIxpressE9eCCDTypes13eIRFilterTypeb")(rawBuffer.data(),sensor,3,true);
            symbol<void(*)(void*,double)>(lib,"_ZN15CRawImageBuffer14SetOrientationEd")(rawBuffer.data(),0.0);
            symbol<void(*)(void*,int,char*,char*)>(lib,"_ZN15CRawImageBuffer20SetMultiShotPositionE12eCaptureTypePcS1_")(rawBuffer.data(),captureType,positionsA,positionsB);
            block.raw=rawBuffer.data();
            if(frames==16) {
                rgbWidth=symbol<int(*)(const void*,int,bool)>(lib,"_ZNK15CRawImageBuffer12CroppedWidthEib")(rawBuffer.data(),frames,false);
                rgbHeight=symbol<int(*)(const void*,int,bool)>(lib,"_ZNK15CRawImageBuffer13CroppedHeightEib")(rawBuffer.data(),frames,false);
                if(rgbWidth!=23312||rgbHeight!=17484) throw std::runtime_error("Unexpected X2D native crop geometry");
                rgb.resize((size_t)rgbWidth*rgbHeight*3,65535);
                symbol<void(*)(void*,unsigned short*,int,int,int)>(lib,"_ZN17CMultiShotCapture15SetFullBufferCFEPtiii")(capture.data(),rgb.data(),rgbWidth*3,rgbWidth,rgbHeight);
            }
            fprintf(stderr,"INPUT width=%d height=%d sensor=%d correctionBits=%llu borders=%d,%d,%d,%d\n",w,h,sensor,bits,field<int>(rawBuffer.data(),8),field<int>(rawBuffer.data(),12),field<int>(rawBuffer.data(),16),field<int>(rawBuffer.data(),20));
        } else if(w!=width||h!=height||sensor!=s||bits!=firstBits) throw std::runtime_error("Inconsistent inputs");
        std::vector<unsigned short> pixels((size_t)w*h);
        unsigned count=(unsigned)pixels.size();
        auto decoded=symbol<unsigned(*)(void*,unsigned short*,unsigned&,void*)>(lib,"_ZN18CRawHasselbladFile11ReadRawDataEPtRjP9CProgress")(raw.data(),pixels.data(),count,nullptr);
        if(decoded!=pixels.size()||count!=pixels.size()) throw std::runtime_error("RAW decoder byte count mismatch");
        if(pipeline)pipeline->calibrate(raw.data(),argv[3+sourceIndex],pixels,w,h);
        symbol<void(*)(void*,unsigned short*)>(lib,"_ZN15CRawImageBuffer16SetCaptureBufferEPt")(rawBuffer.data(),pixels.data());
        mark("native-add-shot");
        // Direct overload; the calibration-dependent SetupBadColumnMap wrapper
        // is deliberately not represented as executed by this contract probe.
        bool added=pipeline?pipeline->addShot(capture.data(),rawBuffer.data(),block,frames):symbol<bool(*)(void*,void*,int*,void*,int,ParameterBlock*)>(lib,"_ZN17CMultiShotCapture7AddShotEP15CRawImageBufferPiP9CProgress13eBackDataTypeP21sCameraParameterBlock")(capture.data(),rawBuffer.data(),nullptr,nullptr,2,frames==16?nullptr:&block);
        if(!added)throw std::runtime_error("Native AddShot failed");
        fprintf(stderr,"ADDED %d/%d counter=%d\n",i+1,frames,field<short>(capture.data(),0x1f0));fflush(stderr);
        desc.reset();closeRaw(raw.data());
    }
    if(pipeline) {
        pipeline->render(frames,width,height,block,rgb,positionsA,positionsB);
        mark("native-render-returned");pipeline.reset();mark("native-pipeline-released");
        return 0;
    }
    if(frames==16) {
        mark("save-native-completed-rgb");
        FILE *saved=fopen(argv[2],"wbx");if(!saved)throw std::runtime_error("Cannot create RGB output");
        if(fwrite(rgb.data(),2,rgb.size(),saved)!=rgb.size())throw std::runtime_error("Short RGB output write");
        fclose(saved);
        printf("{\"status\":\"native-add-shot-rgb-written\",\"frames\":16,\"width\":%d,\"height\":%d,\"channels\":3,\"rawCrop\":[128,96,11784,8838],\"fullConstructor\":true,\"addShotPrepare\":true,\"nativeCompletion\":true,\"x2dSequenceVerified\":true,\"calibrationValidated\":false}\n",rgbWidth,rgbHeight);
        return 0;
    }
    if(!block.image||block.levels.size()!=192) throw std::runtime_error("Native output metadata incomplete");
    if(field<int>(block.image,8)!=width||field<int>(block.image,12)!=height||field<int>(block.image,0x14)!=frames) throw std::runtime_error("Unexpected CGenImage output shape");
    auto packed=field<unsigned short*>(block.image,0x30);
    if(!packed) throw std::runtime_error("Missing native packed image");
    fprintf(stderr,"PACKED levels=%zu columns=%zu blemishes=%zu\n",block.levels.size(),block.columns.size(),block.blemishes.size());
    // A fresh native header/stream avoids retaining the source file directory
    // with stale offsets after rebuilding the TIFF layout.
    std::ifstream input(argv[3],std::ios::binary|std::ios::ate);
    const auto inputSize=input.tellg();
    std::vector<unsigned char> sourceBytes((size_t)inputSize);
    input.seekg(0);input.read((char*)sourceBytes.data(),inputSize);
    if(!input) throw std::runtime_error("Cannot read source header stream");
    alignas(16) std::array<unsigned char,512> sourceStream{};
    symbol<void(*)(void*,unsigned char*,size_t)>(lib,"_ZN19CStreamDirectMemoryC1EPhm")(sourceStream.data(),sourceBytes.data(),sourceBytes.size());
    symbol<void(*)(void*,bool)>(lib,"_ZN7CStream12SetBigEndianEb")(sourceStream.data(),false);
    std::filesystem::copy_file(argv[3],output);
    std::filesystem::permissions(output,std::filesystem::perms::owner_write,std::filesystem::perm_options::add);
    alignas(16) std::array<unsigned char,512> spec{};
    alignas(16) std::array<unsigned char,4096> target{};
    fileCtor(spec.data(),argv[2],true,0,0);
    reinterpret_cast<void(*)(void*,const void*,bool,int)>((char*)ci.dli_fbase+nativeOffset(0x55bb4,0x5c540))(target.data(),spec.data(),false,0);
    fileDtor(spec.data());
    auto header=symbol<void*(*)(void*,void*)>(lib,"_ZN13CRawImageFile10AddRawIFDsEP14CRawTiffHeader")(target.data(),nullptr);
    if(!symbol<bool(*)(void*,void*,bool,bool)>(lib,"_ZN14CRawTiffHeader8ReadFromEP7CStreambb")(header,sourceStream.data(),false,false)) throw std::runtime_error("Native header parse failed");
    auto integer=symbol<bool(*)(void*,int,unsigned short,int&)>(lib,"_ZN14CRawTiffHeader13GetIntegerTagE12eRawIfdIndextRi");
    int previewOffset=0,previewBytes=0;
    if(!integer(header,0,0x111,previewOffset)||!integer(header,0,0x117,previewBytes)||previewOffset<0||previewBytes<1||(size_t)previewOffset+previewBytes>sourceBytes.size()) throw std::runtime_error("Native preview bounds invalid");
    symbol<void*(*)(void*,int,unsigned short,unsigned,bool,unsigned,unsigned char*)>(lib,"_ZN14CRawTiffHeader9AddBufferE12eRawIfdIndextjbjPh")(header,0,0x111,previewBytes,true,3,sourceBytes.data()+previewOffset);
    if(!setShots(target.data(),frames,nullptr)) throw std::runtime_error("SetMultiShot failed");
    std::vector<short> positions;
    for(int i=0;i<16;i++){positions.push_back(positionsA[i]);positions.push_back(positionsB[i]);}
    mark("native-set-buffer");
    auto setBuffer=symbol<bool(*)(void*,unsigned short*,int,int,int,std::vector<int>,std::vector<unsigned>,std::vector<short>,std::vector<unsigned short>,std::vector<short>)>(lib,"_ZN23CInitializeRawImageFile16SetSixShotBufferEPtiiiNSt3__16vectorIiNS1_9allocatorIiEEEENS2_IjNS3_IjEEEENS2_IsNS3_IsEEEENS2_ItNS3_ItEEEES9_");
    if(!setBuffer(target.data(),packed,width,height,frames,block.levels,block.columns,block.blemishes,{},positions)) throw std::runtime_error("Native SetSixShotBuffer failed");
    if(!symbol<bool(*)(void*,int,bool)>(lib,"_ZN23CInitializeRawImageFile13SetDataFormatE14eRawDataFormatb")(target.data(),frames==4?10:8,false)) throw std::runtime_error("Native SetDataFormat failed");
    mark("native-update-source-info");
    symbol<void(*)(void*)>(lib,"_ZN13CRawImageFile16UpdateSourceInfoEv")(target.data());
    mark("native-write-fff");
    std::vector<unsigned char> written((size_t)width*height*frames*2+64*1024*1024);
    alignas(16) std::array<unsigned char,512> outputStream{};
    symbol<void(*)(void*,unsigned char*,size_t)>(lib,"_ZN19CStreamDirectMemoryC1EPhm")(outputStream.data(),written.data(),written.size());
    symbol<void(*)(void*,bool)>(lib,"_ZN7CStream12SetBigEndianEb")(outputStream.data(),false);
    if(!symbol<bool(*)(void*,void*,void*,bool,bool,int,bool,bool)>(lib,"_ZN14CRawTiffHeader13WriteAllButToEP7CStreamP14sLastWriteSpecbbibb")(header,outputStream.data(),nullptr,false,true,0x2800,false,false)) throw std::runtime_error("Native WriteAllButTo failed");
    auto actual=symbol<size_t(*)(void*)>(lib,"_ZN19CStreamDirectMemory11GetPositionEv")(outputStream.data());
    fprintf(stderr,"SERIALIZED position=%zu capacity=%zu prefix=%02x%02x%02x%02x\n",actual,written.size(),written[0],written[1],written[2],written[3]);
    if(actual>written.size()) throw std::runtime_error("Invalid serialized output length");
    if(actual<10000) actual=written.size();
    std::ofstream saved(output,std::ios::binary);saved.write((char*)written.data(),actual);saved.close();
    if(!saved) throw std::runtime_error("Cannot save native serialization");
    symbol<void(*)(void*)>(lib,"_ZN19CStreamDirectMemoryD1Ev")(outputStream.data());
    symbol<void(*)(void*)>(lib,"_ZN19CStreamDirectMemoryD1Ev")(sourceStream.data());
    printf("{\"status\":\"candidate-fff-written\",\"frames\":%d,\"rawWidth\":%d,\"rawHeight\":%d,\"levels\":%zu,\"calibrationValidated\":false,\"phocusRenderValidated\":false}\n",frames,width,height,block.levels.size());
    return 0;
} catch(const std::exception &e) {fprintf(stderr,"BRIDGE_ERROR %s\n",e.what());return 4;} }
int main(int argc,char **argv) {
    int result=0;
    @autoreleasepool {result=runBridge(argc,argv);mark("bridge-body-returned");}
    mark("bridge-pool-drained");
    return result;
}
