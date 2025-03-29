MRuby::CrossBuild.new("macos-arm64") do |conf|
  conf.toolchain :clang

  conf.gem core: 'mruby-compiler'
  # conf.gem core: 'mruby-bin-mrbc'    
  conf.gem './mrbgems/mrbcs-compiler'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM)
  conf.cc.flags << '-arch arm64'
  conf.linker.flags << '-arch arm64'
end

MRuby::CrossBuild.new("macos-x64") do |conf|
  conf.toolchain :clang
  
  conf.gem core: 'mruby-compiler'
  conf.gem core: 'mruby-bin-mrbc'    
  conf.gem './mrbgems/mrbcs-compiler'  

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM)
  conf.cc.flags << '-arch x86_64'
  conf.linker.flags << '-arch x86_64'
end
