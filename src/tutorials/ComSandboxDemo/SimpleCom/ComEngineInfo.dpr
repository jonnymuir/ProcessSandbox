library ComEngineInfo;

{$MODE DELPHI}

uses
  Windows, ActiveX, SysUtils;

const
  // Unique CLSID for the Info Engine
  CLASS_ComEngineInfo: TGUID = '{B1E9D2C4-8A6F-4E2B-9D3D-1234567890AB}';
  IID_IEngineInfo: TGUID    = '{A1B2C3D4-E5F6-4A5B-9C8D-7E6F5A4B3C2D}';

type
  IEngineInfo = interface(IUnknown)
    ['{A1B2C3D4-E5F6-4A5B-9C8D-7E6F5A4B3C2D}']
    function GetEngineName: WideString; stdcall;
  end;

  { The Implementation }
  TComEngineInfo = class(TInterfacedObject, IEngineInfo)
  public
    function GetEngineName: WideString; stdcall;
  end;

  { The Factory }
  TComEngineInfoFactory = class(TInterfacedObject, IClassFactory)
  public
    function CreateInstance(const unkOuter: IUnknown; const iid: TGUID; out obj): HResult; stdcall;
    function LockServer(fLock: BOOL): HResult; stdcall;
  end;

{ TComEngineInfo }

function TComEngineInfo.GetEngineName: WideString; stdcall;
begin
  Result := 'Running the ComEngineInfo Delphi FPC COM object';
end;

{ TComEngineInfoFactory }

function TComEngineInfoFactory.CreateInstance(const unkOuter: IUnknown; const iid: TGUID; out obj): HResult; stdcall;
var
  EngineIntf: IEngineInfo;
begin
  Pointer(obj) := nil;
  if unkOuter <> nil then Exit(CLASS_E_NOAGGREGATION);

  try
    EngineIntf := TComEngineInfo.Create;
    Result := EngineIntf.QueryInterface(iid, obj);
  except
    Result := E_UNEXPECTED;
  end;
end;

function TComEngineInfoFactory.LockServer(fLock: BOOL): HResult; stdcall;
begin
  Result := S_OK;
end;

{ DLL Exports }

function DllGetClassObject(const clsid, iid: TGUID; out obj): HResult; stdcall;
var
  FactoryIntf: IClassFactory;
begin
  Pointer(obj) := nil;
  if IsEqualGUID(clsid, CLASS_ComEngineInfo) then
  begin
    FactoryIntf := TComEngineInfoFactory.Create;
    Result := FactoryIntf.QueryInterface(iid, obj);
  end
  else
    Result := CLASS_E_CLASSNOTAVAILABLE;
end;

function DllCanUnloadNow: HResult; stdcall;
begin
  Result := S_FALSE;
end;

exports
  DllGetClassObject,
  DllCanUnloadNow;

begin
end.