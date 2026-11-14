'use strict';
(function(root){
  const clamp=(value,min,max)=>Math.max(min,Math.min(max,value));
  function fit(width,height,scale,availableWidth,availableHeight,gap=16){
    return Math.max(.1,Math.min(scale,(availableWidth-gap*2)/width,(availableHeight-gap*2)/height));
  }
  function measure({width,height,screen=100,touch=false,reserve=0,insets={},sizes={}}){
    const factor=key=>Number.isFinite(sizes?.[key])?clamp(sizes[key]/100,.6,1.8):1;
    const safe={left:0,right:0,top:0,bottom:0,...insets};
    const viewportHeight=Math.max(1,height-reserve);
    const safeWidth=Math.max(1,width-safe.left-safe.right);
    const safeHeight=Math.max(1,viewportHeight-safe.top-safe.bottom);
    const tablet=touch&&Math.min(width,height)>=600;
    const profile=tablet?'tablet':touch?'phone':'desktop';
    const preference=Number.isFinite(screen)?clamp(screen/100,.8,1.5):1;
    const stageFit=Math.min(safeWidth/568,safeHeight/320);
    const baseUiScale=Math.min(stageFit,stageFit*preference,tablet?2.5:3);
    const uiScale=Math.min(baseUiScale*factor('dialogs'),Math.max(baseUiScale,fit(476,274,Infinity,safeWidth,safeHeight)));
    // Scale controls uniformly while allowing edge anchors to reach the viewport.
    const menuScale=Math.min(stageFit,stageFit*preference*factor('menu'));
    // Corner size depends on the viewport, never on the space removed by safe insets.
    const legacyScale=Math.min(Math.max(Math.min(width/568,height/320)*preference,48/27,height/300),width/420,height/180);
    const baseCornerSize=clamp(16*legacyScale,tablet?48:40,tablet?68:60)*1.06;
    const cornerSize=Math.min(baseCornerSize*factor('buttons'),safeWidth/5,safeHeight/3);
    const battleScale=cornerSize/20;
    const baseActionSize=clamp(baseCornerSize*1.15,tablet?48:40,tablet?60:56);
    const actionSize=baseActionSize*factor('actions');
    const compactScale=actionSize/33;
    const cornerInset=touch?6:4;
    const gutter=cornerInset+cornerSize+8;
    const actionWidth=Math.max(actionSize,width-gutter*2)/compactScale;
    const hudScale=Math.min(baseActionSize/33*factor('info'),Math.max(1,(width-cornerSize-32)/164));
    // Insets constrain floating content, not the viewport used by the map and edge HUD.
    return {profile,orientation:height>=width?'portrait':'landscape',width,height:viewportHeight,safeWidth,safeHeight,
      uiScale,settingsScale:baseUiScale,menuScale,menuWidth:width/menuScale,menuHeight:viewportHeight/menuScale,
      menuSafeLeft:safe.left/menuScale,menuSafeRight:safe.right/menuScale,
      menuSafeTop:safe.top/menuScale,menuSafeBottom:safe.bottom/menuScale,
      battleScale,compactScale,hudScale,aiCountryScale:baseUiScale*1.06,cornerSize,cornerInset,actionWidth,
      pauseScale:fit(168,274,uiScale,safeWidth,safeHeight),
      talkScale:fit(339,96,Math.max(1,baseUiScale)*factor('dialogue'),safeWidth,safeHeight,10)};
  }
  root.Ew4ScreenLayout={measure,fit};
})(globalThis);
