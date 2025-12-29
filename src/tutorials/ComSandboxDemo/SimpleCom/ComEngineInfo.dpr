library ComEngineInfo;

uses
  ComServ, ComObj, ActiveX, Windows, SysUtils;

const
  // Unique CLSID for the Info Engine
  CLASS_ComEngineInfo: TGUID = '{B1E9D2C4-8A6F-4E2B-9D3D-1234567890AB}';

type
  // Simple interface for our internal engine
  IEngineInfo = interface(IUnknown)
    ['{A1B2C3D4-E5F6-4A5B-9C8D-7E6F5A4B3C2D}']
    function GetEngineName: WideString; stdcall;
  end;

  TComEngineInfo = class(TComObject, IEngineInfo)
  protected
    function GetEngineName: WideString; stdcall;
  end;

function TComEngineInfo.GetEngineName: WideString;
begin
  Result := 'Running the ComEngineInfo Delphi FPC COM object';
end;

// Factory to allow the COM system to create this object
type
  TComEngineInfoFactory = class(TComObjectFactory)
  end;

initialization
  TComEngineInfoFactory.Create(ComServer, TComEngineInfo, CLASS_ComEngineInfo,
    'ComEngineInfo', 'Delphi Engine Info Object', ciMultiInstance, tmApartment);
end.