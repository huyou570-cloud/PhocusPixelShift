// One-off ABI probe for the installed, hash-locked Phocus 4.1.1 universal build.
// Native objects and metadata are used throughout. No source photo is written.
#include <mach-o/dyld.h>
#include <algorithm>
#include <cmath>
#include <execinfo.h>
#include <signal.h>
#include <sys/ucontext.h>
#include <unistd.h>
#include <functional>
#include <atomic>
#include <chrono>
#include <CommonCrypto/CommonDigest.h>

struct PipelineImage { virtual ~PipelineImage(); };
struct PipelineRect { alignas(8) unsigned char data[32]{}; ~PipelineRect(){} };
struct PipelineProcessor {
    alignas(8) unsigned char data[32]{};
    inline static void*library=nullptr;
    PipelineProcessor()=default;
    PipelineProcessor(const PipelineProcessor&)=delete;
    PipelineProcessor(PipelineProcessor&&other)noexcept {memcpy(data,other.data,32);memset(other.data,0,32);}
    ~PipelineProcessor(){if(library)symbol<void(*)(void*)>(library,"_ZN15CImageProcessorD1Ev")(data);}
};
template<class T> void store(void*p,size_t n,T v) {memcpy((char*)p+n,&v,sizeof v);}

class NativePipelineProbe {
    void *lib_,*camera_;
    char *imageBase_,*workflowBase_,*rawCorrectionBase_;
    std::shared_ptr<void> outputFile_;
    void *outputHeader_=nullptr;
    int imports_=0;
    double blackStd_=0;
    std::vector<unsigned char>preview_;
    void exportNativeTiff(int frames,std::shared_ptr<void>raw,void*manager,const void*baseCorrection) {
        const char*path=getenv("PHOCUS_NATIVE_TIFF_FILE");if(!path)return;
        const char*sourcePath=getenv("PHOCUS_ROUNDTRIP_FILE");if(!sourcePath)throw std::runtime_error("Native TIFF test requires reopened RAW");
        auto correction=std::shared_ptr<void>(::operator new(2048),[lib=lib_](void*p){symbol<void(*)(void*)>(lib,"_ZN16CImageCorrectionD1Ev")(p);::operator delete(p);});
        symbol<void(*)(void*,const void*)>(lib_,"_ZN16CImageCorrectionC1ERKS_")(correction.get(),baseCorrection);
        symbol<void(*)(void*,int)>(lib_,"_ZN16CImageCorrection7SetModeE9eModeType")(correction.get(),1); // Native RGB, 16 bits per channel.
        symbol<void(*)(void*,bool)>(lib_,"_ZN16CImageCorrection22SetWantsToEmbedProfileEb")(correction.get(),true);
        symbol<void(*)(void*,bool)>(lib_,"_ZN16CImageCorrection15SetUseColorSyncEb")(correction.get(),true);
        // The hash-locked native GetRGBProfile implementation identifies this
        // CFileSpec member. Reconstruct it using the native constructor.
        symbol<void(*)(void*)>(lib_,"_ZN9CFileSpecD1Ev")((char*)correction.get()+0xf8);
        symbol<void(*)(void*,const char*,bool,int,int)>(lib_,"_ZN9CFileSpecC1EPKcbii")((char*)correction.get()+0xf8,"/System/Library/ColorSync/Profiles/sRGB Profile.icc",true,0,0);
        auto descriptionStorage=std::make_shared<std::array<unsigned char,512>>();
        auto description=std::shared_ptr<void>(descriptionStorage,descriptionStorage->data());
        symbol<void(*)(void*)>(lib_,"_ZN17CImageDescriptionC1Ev")(description.get());
        const bool full=true; // Native default relative crop and zoom: full frame at 100%.
        auto setting=std::shared_ptr<void>(::operator new(512),[lib=lib_](void*p){symbol<void(*)(void*)>(lib,"_ZN13CImageSettingD1Ev")(p);::operator delete(p);});
        symbol<void(*)(void*,std::shared_ptr<void>,std::shared_ptr<void>)>(lib_,"_ZN13CImageSettingC1ENSt3__110shared_ptrI16CImageCorrectionEENS1_I17CImageDescriptionEE")(setting.get(),correction,description);
        alignas(16)std::array<unsigned char,512>sourceSpec{},destinationSpec{};
        symbol<void(*)(void*,const char*,bool,int,int)>(lib_,"_ZN9CFileSpecC1EPKcbii")(sourceSpec.data(),sourcePath,true,0,0);
        symbol<void(*)(void*,const char*,int,int,int)>(lib_,"_ZN9CFileSpecC1EPKc13eFileSpecPermii")(destinationSpec.data(),path,3,0,0);
        void*factory=*symbol<void**>(lib_,"gStreamFactory");
        void*stream=symbol<void*(*)(void*,const void*)>(lib_,"_ZN17CStreamFactoryMac13NewFileStreamE9CFileSpec")(factory,destinationSpec.data());
        if(!stream)throw std::runtime_error("Native TIFF destination stream unavailable");
        mark("native-high-level-SaveTIFF");auto start=std::chrono::steady_clock::now();
        auto save=symbol<int(*)(const void*,std::shared_ptr<void>,void*,void*,void*,const void*,std::function<void(std::string)>,void*,int,std::shared_ptr<void>,bool,void*)>(lib_,"_ZN16CTiffSaveHandler8SaveTIFFE9CFileSpecNSt3__110shared_ptrI13CImageSettingEEP13CColorManagerP13CNativeExportP7CStreamS0_NS1_8functionIFvNS1_12basic_stringIcNS1_11char_traitsIcEENS1_9allocatorIcEEEEEEEP9CProgressiNS2_I13CRawImageFileEEbP22CTaskContextSaveFrom3f");
        int result=save(sourceSpec.data(),setting,manager,nullptr,stream,destinationSpec.data(),{},nullptr,-1,raw,true,nullptr);
        reinterpret_cast<void(*)(void*)>(field<void**>(stream,0)[16])(stream);
        symbol<void(*)(void*)>(lib_,"_ZN9CFileSpecD1Ev")(sourceSpec.data());symbol<void(*)(void*)>(lib_,"_ZN9CFileSpecD1Ev")(destinationSpec.data());
        printf("{\"nativeTiffSave\":true,\"frames\":%d,\"fullFrame\":%s,\"result\":%d,\"mode\":1,\"profile\":\"sRGB\",\"metadataTransferRequested\":true,\"elapsedSeconds\":%.9g,\"imageRetained\":false}\n",frames,full?"true":"false",result,std::chrono::duration<double>(std::chrono::steady_clock::now()-start).count());fflush(stdout);
        if(result)throw std::runtime_error("Native TIFF save returned an error");
        mark("export-setting-release");setting.reset();
        mark("export-description-release");description.reset();descriptionStorage.reset();
        mark("export-correction-release");correction.reset();
        mark("export-raw-reference-release");raw.reset();
        mark("export-release-complete");
    }
public:
    NativePipelineProbe(void*lib,void*camera):lib_(lib),camera_(camera) {
        struct sigaction action{};
        action.sa_flags=SA_SIGINFO;
        action.sa_sigaction=[](int,siginfo_t*info,void*context){
#if defined(__x86_64__)
            auto uc=static_cast<ucontext_t*>(context);
            auto pc=uc->uc_mcontext->__ss.__rip;
            Dl_info fault{};dladdr(reinterpret_cast<void*>(pc),&fault);
            fprintf(stderr,"NATIVE_FAULT address=%p rip=%llx module=%s base=%p offset=%llx symbol=%s\n",info->si_addr,pc,fault.dli_fname?fault.dli_fname:"?",fault.dli_fbase,pc-(unsigned long long)fault.dli_fbase,fault.dli_sname?fault.dli_sname:"?");
#endif
            void*a[40];int n=backtrace(a,40);backtrace_symbols_fd(a,n,2);_exit(139);
        };
        sigaction(SIGSEGV,&action,nullptr);
        Dl_info info{};dladdr(symbol<void*>(lib_,"_ZN15CImageProcessorC1Ev"),&info);imageBase_=(char*)info.dli_fbase;
        if(!dlopen("/Applications/Phocus.app/Contents/Frameworks/HBWorkFlow.framework/Versions/A/HBWorkFlow",RTLD_NOW|RTLD_GLOBAL))throw std::runtime_error(dlerror());
        workflowBase_=rawCorrectionBase_=nullptr;
        for(unsigned i=0;i<_dyld_image_count();i++) {
            const auto n=_dyld_get_image_name(i);auto b=(char*)_dyld_get_image_header(i);
            if(strstr(n,"HBWorkFlow.framework/Versions/A/HBWorkFlow"))workflowBase_=b;
            if(strstr(n,"HBRawCorrections.framework/Versions/A/HBRawCorrections"))rawCorrectionBase_=b;
        }
        if(!workflowBase_||!rawCorrectionBase_)throw std::runtime_error("Missing native library base");
        symbol<void(*)(size_t,size_t,size_t)>(lib_,"_ZN19CRawCacheController4InitEmmm")(1024UL*1024*1024,0,0);
    }

    void calibrate(void*source,const char*path,std::vector<unsigned short>&pixels,int w,int h) {
        mark("native-calibration-context");
        std::ifstream input(path,std::ios::binary|std::ios::ate);auto size=input.tellg();
        std::vector<unsigned char>bytes((size_t)size);input.seekg(0);input.read((char*)bytes.data(),size);
        if(!input)throw std::runtime_error("Cannot read calibration source");
        alignas(16)std::array<unsigned char,512>stream{};
        symbol<void(*)(void*,unsigned char*,size_t)>(lib_,"_ZN19CStreamDirectMemoryC1EPhm")(stream.data(),bytes.data(),bytes.size());
        symbol<void(*)(void*,bool)>(lib_,"_ZN7CStream12SetBigEndianEb")(stream.data(),false);
        auto bits=symbol<unsigned long long(*)(void*)>(lib_,"_ZN13CRawImageFile20GetRawCorrectionBitsEv")(source);
        void*cal=symbol<void*(*)(void*,void*,unsigned long long)>(camera_,"_ZN9CTransfer19RawCorrectionObjectEP13CRawImageFileP7CStreamy")(source,stream.data(),bits|0x1800);
        if(!cal||!symbol<bool(*)(void*)>(lib_,"_ZNK21CIxpressCFCalibration16AbleToRawCorrectEv")(cal))throw std::runtime_error("Native calibration unavailable");
        double gain[3]{};symbol<void(*)(void*,double*)>(lib_,"_ZNK21CIxpressCFCalibration18AppliedNeutralgainEPd")(cal,gain);
        if(!outputFile_) {
            alignas(16)std::array<unsigned char,512>spec{};
            symbol<void(*)(void*,const char*,bool,int,int)>(lib_,"_ZN9CFileSpecC1EPKcbii")(spec.data(),path,true,0,0);
            void*owned=::operator new(4096);memset(owned,0,4096);
            // Real CInitializeRawImageFile (derived from CRaw3FFile) constructor,
            // matching the native destination class used by CCameraData.
            reinterpret_cast<void(*)(void*,const void*,bool,int)>(workflowBase_+nativeOffset(0x202c0,0x21080))(owned,spec.data(),true,0);
            symbol<void(*)(void*)>(lib_,"_ZN9CFileSpecD1Ev")(spec.data());
            // Native unique_ptr -> shared_ptr constructor also initializes
            // CRawImageFile::enable_shared_from_this, required by ProcessCrop.
            reinterpret_cast<void(*)(void*,void**)>(workflowBase_+nativeOffset(0x1ba50,0x1d97e))(&outputFile_,&owned);
            if(owned||!outputFile_)throw std::runtime_error("Native ownership construction failed");
            if(symbol<short(*)(void*,bool)>(lib_,"_ZN13CRawImageFile4OpenEb")(outputFile_.get(),true)!=0)throw std::runtime_error("Cannot open read-only native context");
            outputHeader_=symbol<void*(*)(void*,void*)>(lib_,"_ZN13CRawImageFile10AddRawIFDsEP14CRawTiffHeader")(outputFile_.get(),nullptr);
            auto captureStorage=std::shared_ptr<void>(::operator new(4096),[lib=lib_](void*p){symbol<void(*)(void*)>(lib,"_ZN15CRawCaptureInfoD1Ev")(p);::operator delete(p);});
            symbol<void(*)(void*,void*)>(lib_,"_ZN15CRawCaptureInfoC1EP13CRawImageFile")(captureStorage.get(),source);
            // Preserve the actual native capture context; no fields are invented.
            *reinterpret_cast<std::shared_ptr<void>*>((char*)outputFile_.get()+0xd8)=captureStorage;
        }
        alignas(16)std::array<unsigned char,512>scratchHeader{};
        void*targetHeader=imports_==0?outputHeader_:scratchHeader.data();
        if(imports_!=0)symbol<void(*)(void*)>(lib_,"_ZN14CRawTiffHeaderC1Ev")(targetHeader);
        alignas(16)std::array<unsigned char,1024>data{};
        symbol<void(*)(void*,void*,void*,bool,void*)>(camera_,"_ZN11CCameraDataC1EP23CInitializeRawImageFileP15CCameraDataNextbP14CRawTiffHeader")(data.data(),nullptr,nullptr,false,targetHeader);
        symbol<void(*)(void*,void*)>(camera_,"_ZN11CCameraData9SetSourceEP13CRawImageFile")(data.data(),source);
        store(data.data(),0x30,(unsigned char*)pixels.data());store<unsigned>(data.data(),0x38,(unsigned)(pixels.size()*2));store(data.data(),0x98,cal);
        if(!symbol<bool(*)(void*,void*,bool)>(camera_,"_ZN11CCameraData14CopyFromSourceEP14CRawTiffHeaderb")(data.data(),field<void*>(source,0x40),true))throw std::runtime_error("Native header import failed");
        if(imports_==0) {
            auto integer=symbol<bool(*)(void*,int,unsigned short,int&)>(lib_,"_ZN14CRawTiffHeader13GetIntegerTagE12eRawIfdIndextRi");
            int offset=0,length=0;void*header=field<void*>(source,0x40);
            if(!integer(header,0,0x111,offset)||!integer(header,0,0x117,length)||offset<0||length<1||(size_t)offset+length>bytes.size())throw std::runtime_error("Original native preview bounds invalid");
            preview_.assign(bytes.begin()+offset,bytes.begin()+offset+length);
            if(!symbol<void*(*)(void*,int,unsigned short,unsigned,bool,unsigned,unsigned char*)>(lib_,"_ZN14CRawTiffHeader9AddBufferE12eRawIfdIndextjbjPh")(targetHeader,0,0x111,length,true,3,preview_.data()))throw std::runtime_error("Native preview binding failed");
        }
        if(!symbol<bool(*)(void*,unsigned long long,unsigned long long)>(camera_,"_ZN11CCameraData17SetCorrectionMetaEyy")(data.data(),bits,0x1800))throw std::runtime_error("Native correction metadata rejected");
        std::vector<unsigned short>corrected(pixels.size(),65535);
        symbol<void(*)(void*,unsigned short*,int,int)>(camera_,"_ZN11CCameraData20SetDestinationBufferEPtii")(data.data(),corrected.data(),w,h);
        mark("native-calibrate-decoded-pixels");
        int result=symbol<int(*)(void*,int,void*,bool,bool)>(camera_,"_ZN11CCameraData10RunAndSaveE14eRawDataFormatP9CProgressbb")(data.data(),1,nullptr,false,true);
        if(result)throw std::runtime_error("Native pixel import failed");
        unsigned long changed=0;int maxDelta=0;
        for(size_t j=0;j<pixels.size();j++){int d=(int)corrected[j]-pixels[j];changed+=d!=0;maxDelta=std::max(maxDelta,std::abs(d));}
        fprintf(stderr,"CALIBRATED frame=%d result=%d gain=%.12g,%.12g,%.12g changed=%lu maxDelta=%d\n",imports_+1,result,gain[0],gain[1],gain[2],changed,maxDelta);
        pixels.swap(corrected);
        if(imports_==0) {
            alignas(16)std::array<unsigned char,256>noiseImage{};
            symbol<void(*)(void*,unsigned,unsigned,unsigned,unsigned,unsigned short*,bool,unsigned)>(lib_,"_ZN14CBufferedImageC1EjjjjPtbj")(noiseImage.data(),w,h,1,w,pixels.data(),false,0);
            void*cache=symbol<void*(*)()>(lib_,"_ZN14CBlackStdCache11GetInstanceEv")();
            blackStd_=symbol<double(*)(void*,void*,void*)>(lib_,"_ZN14CBlackStdCache13FetchBlackStdEP13CRawImageFileR14CBufferedImage")(cache,source,noiseImage.data());
            symbol<void(*)(void*)>(lib_,"_ZN14CBufferedImageD1Ev")(noiseImage.data());
            if(!std::isfinite(blackStd_)||blackStd_<=0)throw std::runtime_error("Native optical-black noise calculation failed");
            symbol<bool(*)(void*,double)>(lib_,"_ZN13CRawImageFile25SetBlackStandardDeviationEd")(outputFile_.get(),blackStd_);
            fprintf(stderr,"NATIVE_OPTICAL_BLACK_STD %.12g\n",blackStd_);
        }
        imports_++;
        symbol<void(*)(void*)>(camera_,"_ZN11CCameraDataD1Ev")(data.data());
        if(targetHeader!=outputHeader_)symbol<void(*)(void*)>(lib_,"_ZN14CRawTiffHeaderD1Ev")(targetHeader);
        symbol<void(*)(void*)>(lib_,"_ZN21CIxpressCFCalibrationD1Ev")(cal);::operator delete(cal);
        symbol<void(*)(void*)>(lib_,"_ZN19CStreamDirectMemoryD1Ev")(stream.data());
    }

    bool addShot(void*capture,void*raw,ParameterBlock&block,int frames) {
        if(frames!=16)return reinterpret_cast<bool(*)(void*,ParameterBlock*,void*,int)>(rawCorrectionBase_+nativeOffset(0x339fc,0x492b0))(capture,&block,nullptr,2);
        if(!field<void*>(capture,0xd0))reinterpret_cast<void(*)(void*,void*,void*,int,bool)>(rawCorrectionBase_+nativeOffset(0x337dc,0x49058))(capture,nullptr,raw,2,false);
        return symbol<bool(*)(void*,void*,int*,void*,int,ParameterBlock*)>(lib_,"_ZN17CMultiShotCapture7AddShotEP15CRawImageBufferPiP9CProgress13eBackDataTypeP21sCameraParameterBlock")(capture,raw,nullptr,nullptr,2,nullptr);
    }

    void render(int frames,int width,int height,ParameterBlock&block,std::vector<unsigned short>&rgb,char*a,char*b) {
        mark("native-bind-merged-buffer");void*file=outputFile_.get();
        symbol<bool(*)(void*,int,int*)>(lib_,"_ZN23CInitializeRawImageFile12SetMultiShotEiPi")(file,frames,nullptr);
        unsigned short*data=nullptr;int channels=0;
        if(frames!=16) {
            data=field<unsigned short*>(block.image,0x30);channels=frames;
            std::vector<short>positions;for(int i=0;i<16;i++){positions.push_back(a[i]);positions.push_back(b[i]);}
            auto setBuffer=symbol<bool(*)(void*,unsigned short*,int,int,int,std::vector<int>,std::vector<unsigned>,std::vector<short>,std::vector<unsigned short>,std::vector<short>)>(lib_,"_ZN23CInitializeRawImageFile16SetSixShotBufferEPtiiiNSt3__16vectorIiNS1_9allocatorIiEEEENS2_IjNS3_IjEEEENS2_IsNS3_IsEEEENS2_ItNS3_ItEEEES9_");
            if(!data||!setBuffer(file,data,width,height,frames,block.levels,block.columns,block.blemishes,{},positions))throw std::runtime_error("Native packed buffer binding failed");
            symbol<bool(*)(void*,int,bool)>(lib_,"_ZN23CInitializeRawImageFile13SetDataFormatE14eRawDataFormatb")(file,frames==4?9:7,false);
        } else {
            data=rgb.data();channels=3;
            PipelineRect nativeCrop;symbol<void(*)(void*,int,int,int,int)>(lib_,"_ZN6CXRectC1Eiiii")(nativeCrop.data,128,96,11784,8838);
            if(!symbol<bool(*)(void*,const void*)>(lib_,"_ZN23CInitializeRawImageFile10SetRawCropE6CXRect")(file,nativeCrop.data))throw std::runtime_error("Native crop metadata rejected");
            // Native SetMultiShotBuffer derives 2x dimensions from the native crop.
            if(!symbol<bool(*)(void*,unsigned short*,bool)>(lib_,"_ZN23CInitializeRawImageFile18SetMultiShotBufferEPtb")(file,data,true))throw std::runtime_error("Native RGB buffer binding failed");
            symbol<bool(*)(void*,int,bool)>(lib_,"_ZN23CInitializeRawImageFile13SetDataFormatE14eRawDataFormatb")(file,5,false);
            width=23312;height=17484;
        }
        alignas(16)std::array<unsigned char,32>rawSize{};symbol<void(*)(void*)>(lib_,"_ZN7CXPointC1Ev")(rawSize.data());
        if(!symbol<bool(*)(void*,void*)>(lib_,"_ZN13CRawImageFile11RawDataSizeER7CXPoint")(file,rawSize.data())||field<int>(rawSize.data(),12)!=width||field<int>(rawSize.data(),16)!=height)throw std::runtime_error("Native output buffer dimensions inconsistent");
        fprintf(stderr,"MERGED_BUFFER size=%d,%d channels=%d\n",width,height,channels);
        // Finalize the native strip layout using the native null stream. This
        // performs the same header layout pass without retaining a large image.
        alignas(16)std::array<unsigned char,512>layoutStream{};
        symbol<void(*)(void*)>(lib_,"_ZN11CStreamNullC1Ev")(layoutStream.data());
        mark("native-finalize-strip-layout");
        if(!symbol<bool(*)(void*,void*,void*,bool,bool,int,bool,bool)>(lib_,"_ZN14CRawTiffHeader13WriteAllButToEP7CStreamP14sLastWriteSpecbbibb")(outputHeader_,layoutStream.data(),nullptr,false,true,0x2800,false,false))throw std::runtime_error("Native strip layout failed");
        unsigned stripOffset=0,stripBytes=0;
        bool nativeRawPresent=symbol<bool(*)(void*,unsigned*,unsigned*)>(lib_,"_ZN13CRawImageFile7RawDataEPjS0_")(file,&stripOffset,&stripBytes);
        fprintf(stderr,"NATIVE_STRIP_LAYOUT rawPresent=%d offset=%u bytes=%u\n",nativeRawPresent,stripOffset,stripBytes);
        symbol<void(*)(void*)>(lib_,"_ZN11CStreamNullD1Ev")(layoutStream.data());
        // UpdateSourceInfo builds the native source and capture records using
        // the preserved source capture context and the new native output header.
        // This object initially opened the single-frame context. Clear its real
        // shared descriptor cache after native setters change the header.
        reinterpret_cast<std::shared_ptr<const void>*>((char*)file+0x158)->reset();
        fprintf(stderr,"MERGED_HEADER shots=%d\n",symbol<int(*)(void*)>(lib_,"_ZN13CRawImageFile8GetShotsEv")(file));
        symbol<void(*)(void*)>(lib_,"_ZN13CRawImageFile16UpdateSourceInfoEv")(file);
        reinterpret_cast<std::shared_ptr<const void>*>((char*)file+0x158)->reset();
        std::shared_ptr<void>roundtripFile;
        bool used64BitReopen=false;
        if(const char*path=getenv("PHOCUS_ROUNDTRIP_FILE")) {
            if(std::filesystem::exists(path))throw std::runtime_error("Refusing to overwrite roundtrip file");
            mark("native-serialize-roundtrip");
            // The native legacy reader checks file bounds with signed int32.
            // Preserve RGB16 and let its own lossless-JPEG writer compress the
            // 16-shot payload below that boundary; never truncate raw samples.
            if(getenv("PHOCUS_LOSSLESS_RGB")&&!symbol<bool(*)(void*,int,unsigned short,int,int)>(lib_,"_ZN14CRawTiffHeader13SetIntegerTagE12eRawIfdIndexti13eTIFFDataType")(outputHeader_,1,0x103,7,3))throw std::runtime_error("Cannot select native lossless compression");
            if(getenv("PHOCUS_USE_NATIVE_FILE_WRITER")) {
                symbol<short(*)(void*)>(lib_,"_ZN13CRawImageFile5CloseEv")(file);
                alignas(16)std::array<unsigned char,512>destination{};
                symbol<void(*)(void*,const char*,bool,int,int)>(lib_,"_ZN9CFileSpecC1EPKcbii")(destination.data(),path,true,0,0);
                symbol<void(*)(void*,const void*)>(lib_,"_ZN13CRawImageFile11SetFileSpecE9CFileSpec")(file,destination.data());
                symbol<void(*)(void*)>(lib_,"_ZN9CFileSpecD1Ev")(destination.data());
                short openResult=symbol<short(*)(void*,bool)>(lib_,"_ZN13CRawImageFile4OpenEb")(file,false);
                fprintf(stderr,"NATIVE_DESTINATION_OPEN result=%d stream=%p directory=%p\n",openResult,field<void*>(file,0x80),field<void*>(file,0x88));
                if(!field<void*>(file,0x80)||!field<void*>(file,0x88))throw std::runtime_error("Native destination stream unavailable");
                symbol<void(*)(void*,bool)>(lib_,"_ZN7CStream12SetBigEndianEb")(field<void*>(file,0x80),true);
                if(!symbol<bool(*)(void*,void*,bool,bool,void*,unsigned)>(lib_,"_ZN23CInitializeRawImageFile11WriteAllButEP14sLastWriteSpecbbP19CLosslessJpegWriterj")(file,nullptr,false,true,nullptr,0x2800))throw std::runtime_error("Native WriteAllBut failed");
                symbol<short(*)(void*)>(lib_,"_ZN13CRawImageFile5CloseEv")(file);
            } else {
            std::vector<unsigned char>written((size_t)width*height*channels*2+64UL*1024*1024);
            alignas(16)std::array<unsigned char,512>outStream{};
            symbol<void(*)(void*,unsigned char*,size_t)>(lib_,"_ZN19CStreamDirectMemoryC1EPhm")(outStream.data(),written.data(),written.size());
            symbol<void(*)(void*,bool)>(lib_,"_ZN7CStream12SetBigEndianEb")(outStream.data(),true);
            if(!symbol<bool(*)(void*,void*,void*,bool,bool,int,bool,bool)>(lib_,"_ZN14CRawTiffHeader13WriteAllButToEP7CStreamP14sLastWriteSpecbbibb")(outputHeader_,outStream.data(),nullptr,false,true,0x2800,false,false))throw std::runtime_error("Native roundtrip serialization failed");
            // Keep the full capacity for this temporary diagnostic. The native
            // writer ends by seeking back to metadata, not the payload end.
            FILE*saved=fopen(path,"wbx");if(!saved)throw std::runtime_error("Cannot create roundtrip file");
            bool ok=fwrite(written.data(),1,written.size(),saved)==written.size();fclose(saved);if(!ok)throw std::runtime_error("Short roundtrip write");
            symbol<void(*)(void*)>(lib_,"_ZN19CStreamDirectMemoryD1Ev")(outStream.data());
            written.clear();written.shrink_to_fit();
            }
            reinterpret_cast<void(*)()>(workflowBase_+nativeOffset(0x23cc,0x2814))();
            reinterpret_cast<void(*)()>(workflowBase_+nativeOffset(0x24c8,0x28e0))();
            alignas(16)std::array<unsigned char,512>spec{};
            symbol<void(*)(void*,const char*,bool,int,int)>(lib_,"_ZN9CFileSpecC1EPKcbii")(spec.data(),path,true,0,0);
            roundtripFile=symbol<std::shared_ptr<void>(*)(const void*)>(lib_,"_ZN13CRawImageFile12FromFileSpecE9CFileSpec")(spec.data());
            if(!roundtripFile&&frames==16&&getenv("PHOCUS_ALLOW_NATIVE_64BIT_REOPEN")) {
                const uintmax_t length=std::filesystem::file_size(path);
                if(length<=INT32_MAX||length>UINT32_MAX)throw std::runtime_error("Unexpected large native file size");
                void*owned=::operator new(4096);memset(owned,0,4096);
                reinterpret_cast<void(*)(void*,const void*,bool,int)>(workflowBase_+nativeOffset(0x202c0,0x21080))(owned,spec.data(),true,0);
                reinterpret_cast<void(*)(void*,void**)>(workflowBase_+nativeOffset(0x1ba50,0x1d97e))(&roundtripFile,&owned);
                if(owned||!roundtripFile||symbol<short(*)(void*,bool)>(lib_,"_ZN13CRawImageFile4OpenEb")(roundtripFile.get(),true)!=0)throw std::runtime_error("Cannot open native large-file context");
                void*header=symbol<void*(*)(void*,void*)>(lib_,"_ZN13CRawImageFile10AddRawIFDsEP14CRawTiffHeader")(roundtripFile.get(),nullptr);
                void*stream=field<void*>(roundtripFile.get(),0x80);
                reinterpret_cast<void(*)(void*,size_t)>(field<void**>(stream,0)[2])(stream,0);
                if(!symbol<bool(*)(void*,void*,bool,bool)>(lib_,"_ZN14CRawTiffHeader8ReadFromEP7CStreambb")(header,stream,false,false))throw std::runtime_error("Native large-file metadata parse failed");
                // Do not narrow length through SetFileSize(int). Independently
                // check the strip in uint64 and verify every decoded sample.
                unsigned offset=0,bytes=0;
                if(!symbol<bool(*)(void*,unsigned*,unsigned*)>(lib_,"_ZN13CRawImageFile7RawDataEPjS0_")(roundtripFile.get(),&offset,&bytes)||uint64_t(offset)+bytes>length||bytes!=(uint64_t)width*height*channels*2)throw std::runtime_error("Native large-file payload bounds invalid");
                symbol<void(*)(void*)>(lib_,"_ZN13CRawImageFile16UpdateSourceInfoEv")(roundtripFile.get());
                reinterpret_cast<std::shared_ptr<const void>*>((char*)roundtripFile.get()+0x158)->reset();
                used64BitReopen=true;
                fprintf(stderr,"NATIVE_LARGE_FILE_ADAPTER length=%ju rawOffset=%u rawBytes=%u\n",length,offset,bytes);
            }
            symbol<void(*)(void*)>(lib_,"_ZN9CFileSpecD1Ev")(spec.data());
            if(!roundtripFile)throw std::runtime_error("Native FFF factory rejected serialized file");
            auto rtDesc=symbol<std::shared_ptr<const void>(*)(void*)>(lib_,"_ZN13CRawImageFile17GetRawDescriptionEv")(roundtripFile.get());
            fprintf(stderr,"NATIVE_ROUNDTRIP_DESC frames=%d channels=%d sensor=%d geometry=%d,%d representation=%d\n",field<int>(rtDesc.get(),0xe0),field<int>(rtDesc.get(),0xe4),field<int>(rtDesc.get(),0x32c),field<int>(rtDesc.get(),0x21c),field<int>(rtDesc.get(),0x220),field<int>(roundtripFile.get(),0xcc));
            if(field<int>(rtDesc.get(),0xe0)!=frames||field<int>(rtDesc.get(),0xe4)!=channels||field<int>(rtDesc.get(),0x32c)!=20)throw std::runtime_error("Reopened FFF descriptor mismatch");
            mark("native-verify-serialized-raw-pixels");
            std::vector<unsigned short>decoded((size_t)width*height*channels);
            unsigned count=(unsigned)decoded.size();
            unsigned actual=symbol<unsigned(*)(void*,unsigned short*,unsigned&,void*)>(lib_,"_ZN18CRawHasselbladFile11ReadRawDataEPtRjP9CProgress")(roundtripFile.get(),decoded.data(),count,nullptr);
            unsigned long mismatch=0,swappedMismatch=0;int maxDelta=0;
            for(size_t i=0;i<decoded.size();i++){mismatch+=decoded[i]!=data[i];swappedMismatch+=decoded[i]!=(unsigned short)((data[i]>>8)|(data[i]<<8));maxDelta=std::max(maxDelta,std::abs((int)decoded[i]-data[i]));}
            printf("{\"roundtripRaw\":true,\"frames\":%d,\"returnedSamples\":%u,\"samples\":%zu,\"mismatch\":%lu,\"swappedMismatch\":%lu,\"maxAbsDelta\":%d}\n",frames,actual,decoded.size(),mismatch,swappedMismatch,maxDelta);fflush(stdout);
            if(actual!=decoded.size()||count!=decoded.size()||mismatch)throw std::runtime_error("Reopened raw pixels differ from native merged buffer");
        }
        auto desc=symbol<std::shared_ptr<const void>(*)(void*)>(lib_,"_ZN13CRawImageFile17GetRawDescriptionEv")(file);
        fprintf(stderr,"MERGED_DESC frames=%d channels=%d sensor=%d representation=%d geometry=%d,%d bits=%llx exposureMs=%.9g integrationMs=%.9g preprocessed=%d blackOffset=%d\n",field<int>(desc.get(),0xe0),field<int>(desc.get(),0xe4),field<int>(desc.get(),0x32c),field<int>(file,0xcc),field<int>(desc.get(),0x21c),field<int>(desc.get(),0x220),symbol<unsigned long long(*)(void*)>(lib_,"_ZN13CRawImageFile20GetRawCorrectionBitsEv")(file),field<double>(desc.get(),0x88),field<double>(desc.get(),0x90),field<unsigned char>(desc.get(),0x388),field<int>(desc.get(),0x130));
        if(field<int>(desc.get(),0xe0)!=frames||field<int>(desc.get(),0xe4)!=channels||field<int>(desc.get(),0x32c)!=20)throw std::runtime_error("Native output descriptor inconsistent");
        auto providerCrop=symbol<PipelineRect(*)(const void*)>(lib_,"_ZN23CRawImageProviderFilter7RawCropERK15sRawDescription")(desc.get());
        const int providerX=field<int>(providerCrop.data,12),providerY=field<int>(providerCrop.data,16);
        const int providerWidth=field<int>(providerCrop.data,20)-providerX,providerHeight=field<int>(providerCrop.data,24)-providerY;
        if(providerX<0||providerY<0||providerWidth<1||providerHeight<1||providerX+providerWidth>width||providerY+providerHeight>height)throw std::runtime_error("Native provider crop outside merged buffer");
        fprintf(stderr,"NATIVE_PROVIDER_CROP %d,%d,%d,%d\n",providerX,providerY,providerWidth,providerHeight);
        auto imageStorage=std::make_shared<std::array<unsigned char,256>>();
        symbol<void(*)(void*,unsigned,unsigned,unsigned,unsigned,unsigned short*,bool,unsigned)>(lib_,"_ZN14CBufferedImageC1EjjjjPtbj")(imageStorage->data(),providerWidth,providerHeight,channels,width*channels,data+((size_t)providerY*width+providerX)*channels,false,0);
        std::shared_ptr<const void>sourceImage(imageStorage,imageStorage->data());
        alignas(16)std::array<unsigned char,32>providerProcessor{};
        symbol<void(*)(void*)>(lib_,"_ZN15CImageProcessorC1Ev")(providerProcessor.data());
        auto provider=reinterpret_cast<std::shared_ptr<void>(*)(void*,std::shared_ptr<const void>&)>(imageBase_+nativeOffset(0xb6580,0xed9f0))(providerProcessor.data(),sourceImage);
        mark("native-color-manager-and-curves");
        alignas(16)std::array<unsigned char,512>manager{},profile{};
        symbol<void(*)(void*)>(lib_,"_ZN19CColorSyncManagerNGC1Ev")(manager.data());symbol<void(*)(void*)>(lib_,"_ZN13CColorManager17SetSharedInstanceEPS_")(manager.data());
        symbol<void(*)(void*,const char*,bool,int,int)>(lib_,"_ZN9CFileSpecC1EPKcbii")(profile.data(),"/System/Library/ColorSync/Profiles/sRGB Profile.icc",true,0,0);
        symbol<void(*)(void*,const void*)>(lib_,"_ZN13CColorManager13SetRGBProfileE9CFileSpec")(manager.data(),profile.data());
        alignas(16)std::array<unsigned char,2048>correction{};symbol<void(*)(void*)>(lib_,"_ZN16CImageCorrectionC1Ev")(correction.data());
        alignas(16)std::array<unsigned char,0x1b8>params{};auto p=params.data();
        symbol<void(*)(void*)>(lib_,"_ZN9CFileSpecC1Ev")(p);symbol<void(*)(void*)>(lib_,"_ZN6CXRectC1Ev")(p+0x38);
        store<double>(p,0x58,1);store<double>(p,0x60,1);store<double>(p,0x68,1);store<unsigned>(p,0xbc,2);store<unsigned long long>(p,0xcc,0xffffffffULL);store<int>(p,0x150,100);store<double>(p,0x170,55);store<double>(p,0x180,80);store<unsigned long long>(p,0x190,~0ULL);store<unsigned long long>(p,0x1a4,1);store<unsigned long long>(p,0x1ac,0x200000001ULL);
        symbol<void(*)(std::shared_ptr<const void>,void*,void*,void*,void*,std::shared_ptr<void>)>(lib_,"_ZN12CCameraImage19UpdateProcessParamsENSt3__110shared_ptrIK15sRawDescriptionEEP14sProcessParamsP13CRawImageFileP16CImageCorrectionP17CImageDescriptionNS1_I11CClarifyMapEE")(desc,p,file,correction.data(),nullptr,{});
        auto curveStorage=std::make_shared<std::array<unsigned char,1024>>();symbol<void(*)(void*,const void*)>(lib_,"_ZN17CCalculatedCurvesC1ERK16CImageCorrection")(curveStorage->data(),correction.data());std::shared_ptr<const void>curves(curveStorage,curveStorage->data());
        const int scale=frames==4?1:2;const int x=5500*scale,y=4000*scale,side=512*scale;
        PipelineRect area;symbol<void(*)(void*,int,int,int,int)>(lib_,"_ZN6CXRectC1Eiiii")(area.data,x,y,x+side,y+side);
        mark("native-merged-process-crop");
        auto process=symbol<std::unique_ptr<PipelineImage>(*)(const void*,std::shared_ptr<void>,std::shared_ptr<const void>,void*,void*,std::shared_ptr<const void>,bool,void*,double,std::shared_ptr<void>,std::shared_ptr<void>,double)>(lib_,"_ZN12CCameraImage11ProcessCropERK6CXRectNSt3__110shared_ptrI13CRawImageFileEENS4_IK15sRawDescriptionEEP14sProcessParamsP16CImageCorrectionNS4_IK17CCalculatedCurvesEEbP13CColorManagerdNS4_I12CImageFilterEENS4_I13CGpuTileCacheEEd");
        auto output=process(area.data,outputFile_,desc,p,correction.data(),curves,false,manager.data(),1.0,provider,{},1.0);
        if(!output||field<int>(output.get(),8)!=side||field<int>(output.get(),12)!=side||field<int>(output.get(),16)!=3)throw std::runtime_error("Native color crop output invalid");
        if(roundtripFile) {
            auto reopenedDesc=symbol<std::shared_ptr<const void>(*)(void*)>(lib_,"_ZN13CRawImageFile17GetRawDescriptionEv")(roundtripFile.get());
            for(size_t offset: {0x88UL,0x90UL,0xe0UL,0xe4UL,0x130UL,0x138UL,0x21cUL,0x220UL,0x244UL,0x32cUL,0x388UL})fprintf(stderr,"DESC_COMPARE offset=%zx memory=%llx reopened=%llx\n",offset,field<unsigned long long>(desc.get(),offset),field<unsigned long long>(reopenedDesc.get(),offset));
            // Parameters own a native CFileSpec. Build a fresh instance rather
            // than copying its nontrivial storage from the in-memory context.
            alignas(16)std::array<unsigned char,0x1b8>reopenedParams{};auto rp=reopenedParams.data();
            symbol<void(*)(void*)>(lib_,"_ZN9CFileSpecC1Ev")(rp);symbol<void(*)(void*)>(lib_,"_ZN6CXRectC1Ev")(rp+0x38);
            store<double>(rp,0x58,1);store<double>(rp,0x60,1);store<double>(rp,0x68,1);store<unsigned>(rp,0xbc,2);store<unsigned long long>(rp,0xcc,0xffffffffULL);store<int>(rp,0x150,100);store<double>(rp,0x170,55);store<double>(rp,0x180,80);store<unsigned long long>(rp,0x190,~0ULL);store<unsigned long long>(rp,0x1a4,1);store<unsigned long long>(rp,0x1ac,0x200000001ULL);
            symbol<void(*)(std::shared_ptr<const void>,void*,void*,void*,void*,std::shared_ptr<void>)>(lib_,"_ZN12CCameraImage19UpdateProcessParamsENSt3__110shared_ptrIK15sRawDescriptionEEP14sProcessParamsP13CRawImageFileP16CImageCorrectionP17CImageDescriptionNS1_I11CClarifyMapEE")(reopenedDesc,rp,roundtripFile.get(),correction.data(),nullptr,{});
            mark("native-reopened-file-process-crop-without-provider");
            if(getenv("PHOCUS_DISABLE_RAW_CACHE"))symbol<void(*)(void*,std::string,double,bool)>(lib_,"_ZN13CDebugManager11SetDebugKeyENSt3__112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEEdb")(*symbol<void**>(lib_,"gDebugManager"),std::string("ignoreRawBufferCache"),1.0,false);
            auto reopened=process(area.data,roundtripFile,reopenedDesc,rp,correction.data(),curves,false,manager.data(),1.0,{}, {},1.0);
            if(!reopened||field<int>(reopened.get(),8)!=side||field<int>(reopened.get(),12)!=side||field<int>(reopened.get(),16)!=3)throw std::runtime_error("Reopened native color crop invalid");
            unsigned long changed=0;int maxDelta=0;double absDelta=0,squareDelta=0;
            for(int yy=0;yy<side;yy++) {
                auto left=symbol<unsigned short*(*)(void*,unsigned,unsigned)>(lib_,"_ZN14CBufferedImage11GetPixelPtrEjj")(output.get(),0,yy);
                auto right=symbol<unsigned short*(*)(void*,unsigned,unsigned)>(lib_,"_ZN14CBufferedImage11GetPixelPtrEjj")(reopened.get(),0,yy);
                for(int xx=0;xx<side*3;xx++){int delta=(int)left[xx]-right[xx];changed+=delta!=0;maxDelta=std::max(maxDelta,std::abs(delta));absDelta+=std::abs(delta);squareDelta+=(double)delta*delta;}
            }
            const double samples=(double)side*side*3;
            printf("{\"roundtrip\":true,\"frames\":%d,\"providerOverride\":false,\"native64BitReopenAdapter\":%s,\"size\":[%d,%d],\"samples\":%.0f,\"changedSamples\":%lu,\"maxAbsDelta\":%d,\"meanAbsDelta\":%.12g,\"rmse\":%.12g}\n",frames,used64BitReopen?"true":"false",side,side,samples,changed,maxDelta,absDelta/samples,std::sqrt(squareDelta/samples));fflush(stdout);
            if(changed)throw std::runtime_error("Reopened native color output differs from cropped in-memory input");
            if(getenv("PHOCUS_VERIFY_FULL_RENDER")) {
                const int fw=field<int>(reopenedDesc.get(),0x21c),fh=field<int>(reopenedDesc.get(),0x220);
                PipelineRect fullArea;symbol<void(*)(void*,int,int,int,int)>(lib_,"_ZN6CXRectC1Eiiii")(fullArea.data,0,0,fw,fh);
                alignas(16)std::array<unsigned char,256>context{};
                symbol<void(*)(void*,unsigned long long,std::shared_ptr<void>)>(lib_,"_ZN14CFilterContextC1E6CArrayIiLi2EiENSt3__110shared_ptrI20CAdjustmentLayerListEE")(context.data(),((unsigned long long)(unsigned)fh<<32)|(unsigned)fw,{});
                alignas(16)std::array<unsigned char,32>destination{};
                store(destination.data(),0,(char*)symbol<void*>(lib_,"_ZTV28CDestinationBufferHandlerCpu")+16);
                PipelineProcessor::library=lib_;std::atomic<unsigned>tiles{0};
                auto setup=symbol<PipelineProcessor(*)(const void*,std::shared_ptr<void>,std::shared_ptr<const void>,void*,const void*,std::shared_ptr<const void>,bool,double,std::shared_ptr<void>,double)>(lib_,"_ZN12CCameraImage16ProcessCropSetupERK6CXRectNSt3__110shared_ptrI13CRawImageFileEENS4_IK15sRawDescriptionEEP14sProcessParamsPK16CImageCorrectionNS4_IK17CCalculatedCurvesEEbdNS4_I12CImageFilterEEd");
                std::function<PipelineProcessor(PipelineRect)>build=[&](PipelineRect tile) {
                    // Match the native ProcessImage callback: native parameter
                    // copy, per-tile rectangle and a distinct work identifier.
                    alignas(16)std::array<unsigned char,0x1b8>tileParams{};
                    reinterpret_cast<void(*)(void*,const void*)>(imageBase_+nativeOffset(0x1aac0,0x1be2e))(tileParams.data(),rp);
                    memcpy(tileParams.data()+0x38,tile.data,32);store<int>(tileParams.data(),0x194,tiles.fetch_add(1)+1);
                    auto processor=setup(tile.data,roundtripFile,reopenedDesc,tileParams.data(),correction.data(),curves,false,1.0,{},1.0);
                    symbol<void(*)(void*,const void*,bool,void*)>(lib_,"_ZN12CCameraImage16AddOutputFiltersEP15CImageProcessorPK16CImageCorrectionbP13CColorManager")(processor.data,correction.data(),false,manager.data());
                    reinterpret_cast<void(*)(void*)>(imageBase_+nativeOffset(0x894f4,0xed07c))(tileParams.data());return processor;
                };
                mark("native-full-frame-threaded-render");auto start=std::chrono::steady_clock::now();
                bool ok=symbol<bool(*)(void*,const void*,const void*,int,std::function<PipelineProcessor(PipelineRect)>,void*,std::string)>(lib_,"_ZN15CImageProcessor19OutputImageThreadedER25CDestinationBufferHandler6CXRectRK14CFilterContextiNSt3__18functionIFS_S2_EEEP9CProgressNS6_12basic_stringIcNS6_11char_traitsIcEENS6_9allocatorIcEEEE")(destination.data(),fullArea.data,context.data(),4,build,nullptr,std::string("native-full-frame-verification"));
                auto full=symbol<std::unique_ptr<PipelineImage>(*)(void*)>(lib_,"_ZN28CDestinationBufferHandlerCpu24ReleaseDestinationBufferEv")(destination.data());
                if(!ok||!full||field<int>(full.get(),8)!=fw||field<int>(full.get(),12)!=fh||field<int>(full.get(),16)!=3)throw std::runtime_error("Native threaded full render invalid");
                CC_SHA256_CTX hash;CC_SHA256_Init(&hash);unsigned char digest[CC_SHA256_DIGEST_LENGTH];unsigned short low=65535,high=0;unsigned long roiChanged=0;int roiMax=0;
                for(int yy=0;yy<fh;yy++) {
                    auto row=symbol<unsigned short*(*)(void*,unsigned,unsigned)>(lib_,"_ZN14CBufferedImage11GetPixelPtrEjj")(full.get(),0,yy);
                    CC_SHA256_Update(&hash,row,fw*6);
                    for(int xx=0;xx<fw*3;xx++){low=std::min(low,row[xx]);high=std::max(high,row[xx]);}
                    if(yy>=y&&yy<y+side){auto crop=symbol<unsigned short*(*)(void*,unsigned,unsigned)>(lib_,"_ZN14CBufferedImage11GetPixelPtrEjj")(reopened.get(),0,yy-y);for(int xx=0;xx<side*3;xx++){int delta=(int)row[x*3+xx]-crop[xx];roiChanged+=delta!=0;roiMax=std::max(roiMax,std::abs(delta));}}
                }
                CC_SHA256_Final(digest,&hash);char hex[65];for(int i=0;i<32;i++)sprintf(hex+i*2,"%02x",digest[i]);
                auto repeated=process(area.data,roundtripFile,reopenedDesc,rp,correction.data(),curves,false,manager.data(),1.0,{}, {},1.0);
                if(!repeated)throw std::runtime_error("Post-full native crop missing");
                unsigned long repeatChanged=0;int repeatMax=0;
                for(int yy=0;yy<side;yy++) {auto a=symbol<unsigned short*(*)(void*,unsigned,unsigned)>(lib_,"_ZN14CBufferedImage11GetPixelPtrEjj")(reopened.get(),0,yy);auto b=symbol<unsigned short*(*)(void*,unsigned,unsigned)>(lib_,"_ZN14CBufferedImage11GetPixelPtrEjj")(repeated.get(),0,yy);for(int xx=0;xx<side*3;xx++){int d=(int)a[xx]-b[xx];repeatChanged+=d!=0;repeatMax=std::max(repeatMax,std::abs(d));}}
                double seconds=std::chrono::duration<double>(std::chrono::steady_clock::now()-start).count();
                printf("{\"fullNativeRender\":true,\"frames\":%d,\"size\":[%d,%d,3],\"pixels\":%llu,\"tiles\":%u,\"threads\":4,\"elapsedSeconds\":%.9g,\"sha256\":\"%s\",\"range\":[%u,%u],\"roiChangedSamples\":%lu,\"roiMaxAbsDelta\":%d,\"postFullRepeatedCropChanged\":%lu,\"postFullRepeatedCropMaxDelta\":%d,\"imageRetained\":false}\n",frames,fw,fh,(unsigned long long)fw*fh,tiles.load(),seconds,hex,low,high,roiChanged,roiMax,repeatChanged,repeatMax);fflush(stdout);
            }
            symbol<void(*)(void*)>(lib_,"_ZN9CFileSpecD1Ev")(rp);
        }
        struct ProcessorTrace {alignas(8) unsigned char bytes[32]{};~ProcessorTrace(){}};
        auto setup=symbol<ProcessorTrace(*)(const void*,std::shared_ptr<void>,std::shared_ptr<const void>,void*,const void*,std::shared_ptr<const void>,bool,double,std::shared_ptr<void>,double)>(lib_,"_ZN12CCameraImage16ProcessCropSetupERK6CXRectNSt3__110shared_ptrI13CRawImageFileEENS4_IK15sRawDescriptionEEP14sProcessParamsPK16CImageCorrectionNS4_IK17CCalculatedCurvesEEbdNS4_I12CImageFilterEEd");
        auto trace=setup(area.data,outputFile_,desc,p,correction.data(),curves,false,1.0,provider,1.0);
        symbol<void(*)(void*,const void*,bool,void*)>(lib_,"_ZN12CCameraImage16AddOutputFiltersEP15CImageProcessorPK16CImageCorrectionbP13CColorManager")(trace.bytes,correction.data(),false,manager.data());
        void*filter=field<void*>(trace.bytes,8);unsigned filterCount=0;
        while(filter&&filterCount++<80){const auto&name=*reinterpret_cast<const std::string*>((char*)filter+0x20);fprintf(stderr,"NATIVE_FILTER reverse=%u name=%s\n",filterCount,name.c_str());filter=field<void*>(filter,0x8);}
        mark("native-trace-destroy");
        symbol<void(*)(void*)>(lib_,"_ZN15CImageProcessorD1Ev")(trace.bytes);
        mark("native-trace-destroyed");
        if(frames==16&&getenv("PHOCUS_DIAGNOSE_REGION_DEPENDENCE")) {
            PipelineProcessor::library=lib_;
            auto calculate=[&](const PipelineRect&region,bool bypass) {
                alignas(16)std::array<unsigned char,0x1b8>local{};
                reinterpret_cast<void(*)(void*,const void*)>(imageBase_+nativeOffset(0x1aac0,0x1be2e))(local.data(),p);
                memcpy(local.data()+0x38,region.data,32);
                auto localSetup=symbol<PipelineProcessor(*)(const void*,std::shared_ptr<void>,std::shared_ptr<const void>,void*,const void*,std::shared_ptr<const void>,bool,double,std::shared_ptr<void>,double)>(lib_,"_ZN12CCameraImage16ProcessCropSetupERK6CXRectNSt3__110shared_ptrI13CRawImageFileEENS4_IK15sRawDescriptionEEP14sProcessParamsPK16CImageCorrectionNS4_IK17CCalculatedCurvesEEbdNS4_I12CImageFilterEEd");
                auto processor=localSetup(region.data,outputFile_,desc,local.data(),correction.data(),curves,false,1.0,provider,1.0);
                symbol<void(*)(void*,const void*,bool,void*)>(lib_,"_ZN12CCameraImage16AddOutputFiltersEP15CImageProcessorPK16CImageCorrectionbP13CColorManager")(processor.data,correction.data(),false,manager.data());
                if(bypass){auto filter=symbol<void*(*)(void*,std::string)>(lib_,"_ZN15CImageProcessor12FilterWithIDENSt3__112basic_stringIcNS0_11char_traitsIcEENS0_9allocatorIcEEEE")(processor.data,std::string("MultiShotProcess"));if(!filter)throw std::runtime_error("Diagnostic filter unavailable");store<unsigned char>(filter,0x38,0);}
                alignas(16)std::array<unsigned char,256>context{};
                symbol<void(*)(void*,unsigned long long,std::shared_ptr<void>)>(lib_,"_ZN14CFilterContextC1E6CArrayIiLi2EiENSt3__110shared_ptrI20CAdjustmentLayerListEE")(context.data(),((unsigned long long)(unsigned)field<int>(desc.get(),0x220)<<32)|(unsigned)field<int>(desc.get(),0x21c),{});
                auto image=symbol<std::unique_ptr<PipelineImage>(*)(void*,const void*,void*,std::shared_ptr<void>,void*,std::string)>(lib_,"_ZN15CImageProcessor11OutputImageE6CXRectP14CFilterContextNSt3__110shared_ptrI13CGpuTileCacheEEP9CProgressNS3_12basic_stringIcNS3_11char_traitsIcEENS3_9allocatorIcEEEE")(processor.data,region.data,context.data(),{},nullptr,std::string("diagnostic-region-dependence"));
                reinterpret_cast<void(*)(void*)>(imageBase_+nativeOffset(0x894f4,0xed07c))(local.data());
                if(!image)throw std::runtime_error("Region diagnostic output missing");return image;
            };
            PipelineRect larger;const int padding=128;
            symbol<void(*)(void*,int,int,int,int)>(lib_,"_ZN6CXRectC1Eiiii")(larger.data,x-padding,y-padding,x+side+padding,y+side+padding);
            for(bool bypass:{false,true}) {
                auto small=calculate(area,bypass),large=calculate(larger,bypass);unsigned long changed=0;int maxDelta=0;double square=0;
                for(int yy=32;yy<side-32;yy++){auto a=symbol<unsigned short*(*)(void*,unsigned,unsigned)>(lib_,"_ZN14CBufferedImage11GetPixelPtrEjj")(small.get(),0,yy);auto b=symbol<unsigned short*(*)(void*,unsigned,unsigned)>(lib_,"_ZN14CBufferedImage11GetPixelPtrEjj")(large.get(),padding,yy+padding);for(int xx=32*3;xx<(side-32)*3;xx++){int delta=(int)a[xx]-b[xx];changed+=delta!=0;maxDelta=std::max(maxDelta,std::abs(delta));square+=(double)delta*delta;}}
                printf("{\"regionDependenceDiagnostic\":true,\"frames\":16,\"MultiShotProcessBypassedForDiagnosticOnly\":%s,\"comparedInnerSize\":[%d,%d],\"changedSamples\":%lu,\"maxAbsDelta\":%d,\"rmse\":%.12g,\"finalPipelineBypassed\":false}\n",bypass?"true":"false",side-64,side-64,changed,maxDelta,std::sqrt(square/((side-64)*(side-64)*3.0)));fflush(stdout);
            }
        }
        double sum[3]{},square[3]{};unsigned short lo[3]={65535,65535,65535},hi[3]{};
        for(int yy=0;yy<side;yy++){auto row=symbol<unsigned short*(*)(void*,unsigned,unsigned)>(lib_,"_ZN14CBufferedImage11GetPixelPtrEjj")(output.get(),0,yy);for(int xx=0;xx<side;xx++)for(int c=0;c<3;c++){double v=row[3*xx+c];sum[c]+=v;square[c]+=v*v;lo[c]=std::min(lo[c],(unsigned short)v);hi[c]=std::max(hi[c],(unsigned short)v);}}
        const double n=(double)side*side;
        const int expectedWidth=frames==4?11656:23312,expectedHeight=frames==4?8742:17484;
        printf("{\"nativeRawBuffer\":[%d,%d,%d],\"nativeDescriptionSize\":[%d,%d],\"descriptionMatchesExpectedOutput\":%s,\"opticalBlackStd\":%.12g,\"fullPhocusExportValidated\":false,\"colorReferenceCompared\":false}\n",width,height,channels,field<int>(desc.get(),0x21c),field<int>(desc.get(),0x220),(field<int>(desc.get(),0x21c)==expectedWidth&&field<int>(desc.get(),0x220)==expectedHeight)?"true":"false",blackStd_);
        printf("{\"status\":\"calibrated-native-merged-color-crop\",\"frames\":%d,\"calibratedFrames\":%d,\"roi\":[%d,%d,%d,%d],\"channels\":3,\"meanRGB\":[%.12g,%.12g,%.12g],\"sdRGB\":[%.12g,%.12g,%.12g],\"minRGB\":[%u,%u,%u],\"maxRGB\":[%u,%u,%u],\"workingSpace\":%d,\"filmCurve\":%d,\"wb\":[%.12g,%.12g,%.12g],\"imageWritten\":false}\n",frames,imports_,x,y,side,side,sum[0]/n,sum[1]/n,sum[2]/n,std::sqrt(square[0]/n-std::pow(sum[0]/n,2)),std::sqrt(square[1]/n-std::pow(sum[1]/n,2)),std::sqrt(square[2]/n-std::pow(sum[2]/n,2)),lo[0],lo[1],lo[2],hi[0],hi[1],hi[2],field<int>(p,0x1b0),field<int>(p,0xbc),field<double>(p,0x58),field<double>(p,0x60),field<double>(p,0x68));
        output.reset();provider.reset();symbol<void(*)(void*)>(lib_,"_ZN15CImageProcessorD1Ev")(providerProcessor.data());
        symbol<void(*)(void*)>(lib_,"_ZN14CBufferedImageD1Ev")(imageStorage->data());
        // The high-level exporter may invalidate transient crop/cache pixels on Intel.
        // Finish all crop diagnostics and release their buffers before export.
        if(roundtripFile)exportNativeTiff(frames,roundtripFile,manager.data(),correction.data());
        mark("native-render-body-complete");
        mark("native-roundtrip-release");roundtripFile.reset();
        mark("native-roundtrip-released");
    }
};
