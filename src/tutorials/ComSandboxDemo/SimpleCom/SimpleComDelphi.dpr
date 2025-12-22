library SimpleComDelphi;

{$MODE DELPHI}

uses
  Windows, 
  ComObj, 
  ActiveX, 
  ComServ;

const
  CLSID_SimpleCalculator: TGUID = '{11111111-2222-3333-4444-555555555555}';

type
  ICalculator = interface(IUnknown)
    ['{E1234567-ABCD-1234-EF12-0123456789AB}']
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall; 
  end;

  TSimpleCalculator = class(TComObject, ICalculator)
  protected
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall;
  end;

{ TSimpleCalculator Implementation }

function TSimpleCalculator.Add(a, b: Integer): Integer; stdcall;
begin
  Result := a + b;
end;

function TSimpleCalculator.GetInfo: WideString; stdcall;
begin
  Result := 'Running the native Delphi (FPC) COM object';
end;

// 3. Factory Registration
initialization
  // We provide all 7 arguments explicitly to match the FPC declaration exactly:
  // 1: ComServer
  // 2: The Class
  // 3: The CLSID
  // 4: Class Name
  // 5: Description
  // 6: Instancing (TClassInstancing)
  // 7: ThreadingModel (TThreadingModel)
  TComObjectFactory.Create(
    ComServer, 
    TSimpleCalculator, 
    CLSID_SimpleCalculator, 
    'SimpleCalculator', 
    'Simple Calculator COM Object',
    ciMultiInstance,
    tmBoth
  );
end.