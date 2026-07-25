# GitHub发布指南

项目已经初始化为Git仓库，默认分支为`main`。发布目录、数据库、日志和测试结果已通过`.gitignore`排除。

## 1. 在GitHub创建空仓库

建议仓库名：

```text
IndustrialVisionStudent
```

创建时不要勾选自动生成README、`.gitignore`或许可证，避免与本地已有文件冲突。

## 2. 配置真实提交身份

在项目目录运行，将内容替换为自己的信息：

```powershell
git config user.name "你的GitHub用户名"
git config user.email "你的GitHub验证邮箱"
```

只在当前项目中生效，不会修改其他仓库。

## 3. 执行推送前审计

```powershell
powershell -ExecutionPolicy Bypass -File scripts\pre-push-check.ps1
```

必须看到：

```text
Pre-push checks passed.
```

该脚本还会生成Windows自包含版本并运行最终EXE的`--self-test`，
验证发布目录中的SQLite、OpenCV本机库和默认配方。

## 4. 查看待提交文件

```powershell
git status
git diff --check
```

确认没有数据库、日志、真实生产图片、访问令牌和发布目录。

## 5. 创建第一次提交

```powershell
git add .
git commit -m "feat: 完成工业视觉上位机 v1.1.0"
```

## 6. 关联远程仓库

将地址替换为自己创建的仓库：

```powershell
git remote add origin https://github.com/你的用户名/IndustrialVisionStudent.git
git remote -v
```

## 7. 推送

```powershell
git push -u origin main
```

如果GitHub要求登录，请使用浏览器授权或Personal Access Token，不要把令牌写入源码、脚本或文档。

## 8. 检查GitHub Actions

打开仓库的`Actions`页面，确认`build-test`工作流完成：

- Restore成功。
- 58项测试通过。
- Publish成功。
- 最终EXE自检成功。
- 生成`IndustrialVisionStudent-win-x64`构建产物。

## 9. 创建版本Release

确认`main`上的Actions全部通过后：

```powershell
git tag -a v1.1.0 -m "IndustrialVisionStudent v1.1.0"
git push origin v1.1.0
```

标签会触发`release`工作流，重新运行测试和最终EXE自检，然后自动创建GitHub Release及Windows x64压缩包。

不要给未经验证的代码重复使用同一个标签。需要修复时更新项目版本并创建新标签。

## 10. 仓库展示与许可证

- [x] 已加入不含敏感信息的主界面截图：`docs/assets/main-window.png`。
- 3～5分钟演示视频。
- GitHub Release及版本说明。
- 根据个人意愿选择MIT、Apache-2.0或“不提供开源许可证”。

许可证属于仓库所有者决定，项目当前没有擅自添加许可证。

## 11. 发布前安全检查

- 不上传真实产线IP和PLC地址。
- 不上传真实产品缺陷图片，除非已经获得授权。
- 不上传`inspection.db`、NG图片、日志和账号令牌。
- 不宣称未经真实硬件验证的毫米精度、Modbus兼容性或产线部署结果。
