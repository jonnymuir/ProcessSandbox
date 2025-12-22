library SimpleComDelphi;

{$MODE DELPHI}

uses
  Windows, ActiveX;

const
  CLSID_SimpleCalculator: TGUID = '{11111111-2222-3333-4444-555555555555}';
  IID_ICalculator: TGUID        = '{E1234567-ABCD-1234-EF12-0123456789AB}';

type
  { The Calculator Implementation }
  TSimpleCalculator = class(TInterfacedObject, IUnknown)
  public
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall;
  end;

  { The Class Factory Implementation }
  TSimpleClassFactory = class(TInterfacedObject, IClassFactory)
  public
    function CreateInstance(const unkOuter: IUnknown; const iid: TGUID; out obj): HResult; stdcall;
    function LockServer(fLock: BOOL): HResult; stdcall;
  end;

{ TSimpleCalculator }

function TSimpleCalculator.Add(a, b: Integer): Integer; stdcall;
begin
  Result := a + b;
end;

function TSimpleCalculator.GetInfo: WideString; stdcall;
begin
  Result := 'Running the manual Delphi FPC COM object';
end;

{ TSimpleClassFactory }

function TSimpleClassFactory.CreateInstance(const unkOuter: IUnknown; const iid: TGUID; out obj): HResult; stdcall;
var
  Calc: TSimpleCalculator;
begin
  if unkOuter <> nil then Exit(CLASS_E_NOAGGREGATION);
  Calc := TSimpleCalculator.Create;
  Result := Calc.QueryInterface(iid, obj);
end;

function TSimpleClassFactory.LockServer(fLock: BOOL): HResult; stdcall;
begin
  Result := S_OK;
end;

{ DLL Exports }

function DllGetClassObject(const clsid, iid: TGUID; out obj): HResult; stdcall;
var
  Factory: TSimpleClassFactory;
begin
  if IsEqualGUID(clsid, CLSID_SimpleCalculator) then
  begin
    Factory := TSimpleClassFactory.Create;
    // We must QueryInterface the factory to the requested IID (usually IClassFactory)
    Result := Factory.QueryInterface(iid, obj);
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