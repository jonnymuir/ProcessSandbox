#include <initguid.h>
#include <windows.h>
#include <objbase.h>
#include <stdlib.h>

// CLSID: {11111111-2222-3333-4444-555555555555}
static const GUID CLSID_SimpleCalculator = { 0x11111111, 0x2222, 0x3333, { 0x44, 0x44, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55 } };
// IID: {E1234567-ABCD-1234-EF12-0123456789AB}
static const GUID IID_ICalculator = { 0xE1234567, 0xABCD, 0x1234, { 0xEF, 0x12, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB } };

// --- 1. COM Object (The Calculator) ---

// Renamed struct to avoid collision with system headers
typedef struct MyCalculatorVtbl {
    HRESULT (__stdcall *QueryInterface)(void*, REFIID, void**);
    ULONG (__stdcall *AddRef)(void*);
    ULONG (__stdcall *Release)(void*);
    int (__stdcall *Add)(void*, int, int);
    BSTR (__stdcall *GetInfo)(void*);
} MyCalculatorVtbl;

typedef struct {
    MyCalculatorVtbl* lpVtbl;
    long count;
} SimpleCalculator;

HRESULT __stdcall QueryInterface(void* this, REFIID riid, void** ppv) {
    if (IsEqualGUID(riid, &IID_IUnknown) || IsEqualGUID(riid, &IID_ICalculator)) {
        *ppv = this;
        ((IUnknown*)this)->lpVtbl->AddRef(this);
        return S_OK;
    }
    *ppv = NULL;
    return E_NOINTERFACE;
}

ULONG __stdcall AddRef(void* this) {
    return InterlockedIncrement(&((SimpleCalculator*)this)->count);
}

ULONG __stdcall Release(void* this) {
    ULONG count = InterlockedDecrement(&((SimpleCalculator*)this)->count);
    if (count == 0) free(this);
    return count;
}

int __stdcall Add(void* this, int a, int b) {
    return a + b;
}

BSTR __stdcall GetInfo(void* this) {
    return SysAllocString(L"Running the native C COM object");
}

static MyCalculatorVtbl CalculatorVtbl = { QueryInterface, AddRef, Release, Add, GetInfo };

// --- 2. Class Factory ---

// Renamed struct to avoid collision with system headers
typedef struct MyClassFactoryVtbl {
    HRESULT (__stdcall *QueryInterface)(void*, REFIID, void**);
    ULONG (__stdcall *AddRef)(void*);
    ULONG (__stdcall *Release)(void*);
    HRESULT (__stdcall *CreateInstance)(void*, IUnknown*, REFIID, void**);
    HRESULT (__stdcall *LockServer)(void*, BOOL);
} MyClassFactoryVtbl;

typedef struct {
    MyClassFactoryVtbl* lpVtbl;
} SimpleClassFactoryStruct;

HRESULT __stdcall Factory_QueryInterface(void* this, REFIID riid, void** ppv) {
    if (IsEqualGUID(riid, &IID_IUnknown) || IsEqualGUID(riid, &IID_IClassFactory)) {
        *ppv = this;
        return S_OK;
    }
    *ppv = NULL;
    return E_NOINTERFACE;
}

ULONG __stdcall Factory_AddRef(void* this) { return 2; }
ULONG __stdcall Factory_Release(void* this) { return 1; }

HRESULT __stdcall Factory_CreateInstance(void* this, IUnknown* pUnkOuter, REFIID riid, void** ppv) {
    if (pUnkOuter != NULL) return CLASS_E_NOAGGREGATION;

    SimpleCalculator* obj = (SimpleCalculator*)malloc(sizeof(SimpleCalculator));
    if (!obj) return E_OUTOFMEMORY;
    
    obj->lpVtbl = &CalculatorVtbl;
    obj->count = 1;

    HRESULT hr = obj->lpVtbl->QueryInterface(obj, riid, ppv);
    obj->lpVtbl->Release(obj); 
    return hr;
}

HRESULT __stdcall Factory_LockServer(void* this, BOOL fLock) { return S_OK; }

static MyClassFactoryVtbl ClassFactoryVtbl = { 
    Factory_QueryInterface, Factory_AddRef, Factory_Release, 
    Factory_CreateInstance, Factory_LockServer 
};

static SimpleClassFactoryStruct SimpleClassFactory = { &ClassFactoryVtbl };

// --- 3. DLL Exports ---

HRESULT __stdcall DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv) {
    if (IsEqualGUID(rclsid, &CLSID_SimpleCalculator)) {
        return SimpleClassFactory.lpVtbl->QueryInterface(&SimpleClassFactory, riid, ppv);
    }
    return CLASS_E_CLASSNOTAVAILABLE;
}

HRESULT __stdcall DllCanUnloadNow() { return S_FALSE; }
BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID reserved) { return TRUE; }